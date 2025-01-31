﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using DotNetty.Common.Utilities;
using ProtoBuf;
using SharpPulsar.Common;
using SharpPulsar.Common.Compression;
using SharpPulsar.Exceptions;
using SharpPulsar.Protocol;
using SharpPulsar.Protocol.Proto;

/// <summary>
/// Licensed to the Apache Software Foundation (ASF) under one
/// or more contributor license agreements.  See the NOTICE file
/// distributed with this work for additional information
/// regarding copyright ownership.  The ASF licenses this file
/// to you under the Apache License, Version 2.0 (the
/// "License"); you may not use this file except in compliance
/// with the License.  You may obtain a copy of the License at
/// 
///   http://www.apache.org/licenses/LICENSE-2.0
/// 
/// Unless required by applicable law or agreed to in writing,
/// software distributed under the License is distributed on an
/// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
/// KIND, either express or implied.  See the License for the
/// specific language governing permissions and limitations
/// under the License.
/// </summary>
namespace SharpPulsar.Batch
{

    /// <summary>
	/// Key based batch message container
	/// 
	/// incoming single messages:
	/// (k1, v1), (k2, v1), (k3, v1), (k1, v2), (k2, v2), (k3, v2), (k1, v3), (k2, v3), (k3, v3)
	/// 
	/// batched into multiple batch messages:
	/// [(k1, v1), (k1, v2), (k1, v3)], [(k2, v1), (k2, v2), (k2, v3)], [(k3, v1), (k3, v2), (k3, v3)]
	/// </summary>
	internal class BatchMessageKeyBasedContainer<T> : AbstractBatchMessageContainer<T>
	{

		private IDictionary<string, KeyedBatch> _batches = new Dictionary<string, KeyedBatch>();
        private static ILoggingAdapter _log;

        public BatchMessageKeyBasedContainer(ActorSystem system)
        {
            _log = system.Log;
        }
		public override bool Add(Message<T> msg, Action<object, Exception> callback)
		{
			if (_log.IsDebugEnabled)
			{
				_log.Debug($"[{TopicName}] [{ProducerName}] add message to batch, num messages in batch so far is {NumMessagesInBatch}");
			}
			NumMessagesInBatch++;
			CurrentBatchSize += msg.Data.Length;
			var key = GetKey(msg);
			if (!_batches.TryGetValue(key, out var part))
			{
				part = new KeyedBatch();
				part.AddMsg(msg, callback);
				part.CompressionType = CompressionType;
				part.Compressor = Compressor;
				part.MaxBatchSize = MaxBatchSize;
				part.TopicName = TopicName;
				part.ProducerName = ProducerName;
				if (!_batches.ContainsKey(key)) _batches.Add(key, part);
				if (msg.Metadata.OriginalMetadata.ShouldSerializeTxnidMostBits() && CurrentTxnidMostBits == -1)
				{
					CurrentTxnidMostBits = (long)msg.Metadata.OriginalMetadata.TxnidMostBits;
				}
				if (msg.Metadata.OriginalMetadata.ShouldSerializeTxnidLeastBits() && CurrentTxnidLeastBits == -1)
				{
					CurrentTxnidLeastBits = (long)msg.Metadata.OriginalMetadata.TxnidLeastBits;
				}
			}
			else
			{
				part.AddMsg(msg, callback);
			}
			return BatchFull;
		}

		public override void Clear()
		{
			NumMessagesInBatch = 0;
			CurrentBatchSize = 0;
			_batches = new Dictionary<string, KeyedBatch>();
		}

		public override bool Empty => _batches.Count == 0;

        public override void Discard(Exception ex)
		{
			try
			{
				// Need to protect ourselves from any exception being thrown in the future handler from the application
				_batches.ToList().ForEach(x => x.Value.FirstCallback(null, ex));
			}
			catch (Exception t)
			{
				_log.Warning($"[{TopicName}] [{ProducerName}] Got exception while completing the callback. Error: {t}");
			}
			_batches.ToList().ForEach(x => ReferenceCountUtil.SafeRelease(x.Value.BatchedMessageMetadataAndPayload));
			Clear();
		}

		public override bool MultiBatches => true;

        private ProducerActor<T>.OpSendMsg<T> CreateOpSendMsg(KeyedBatch keyedBatch)
		{
            var encryptedPayload = keyedBatch.CompressedBatchMetadataAndPayload;
            if (Container.Configuration.EncryptionEnabled && Container.Crypto != null)
            {
                try
                {
                    encryptedPayload = Container.Crypto.Encrypt(Container.Configuration.EncryptionKeys, Container.Configuration.CryptoKeyReader, keyedBatch.MessageMetadata, encryptedPayload);
                }
                catch (PulsarClientException e)
                {
                    // Unless config is set to explicitly publish un-encrypted message upon failure, fail the request
                    if (Container.Configuration.CryptoFailureAction != ProducerCryptoFailureAction.Send)
                        throw;
                    _log.Warning($"[{TopicName}] [{ProducerName}] Failed to encrypt message '{e.Message}'. Proceeding with publishing unencrypted message");
                    encryptedPayload = keyedBatch.CompressedBatchMetadataAndPayload;
                }
            }
            if (encryptedPayload.Length > Container.MaxMessageSize)
            {
                Discard(new PulsarClientException.InvalidMessageException("Message Size is bigger than " + Container.MaxMessageSize + " bytes"));
                return null;
            }

			var numMessagesInBatch = keyedBatch.Messages.Count;
			long currentBatchSizeBytes = 0;
			foreach (var message in keyedBatch.Messages)
			{
				currentBatchSizeBytes += message.Data.Length;
			}

            keyedBatch.MessageMetadata.ProducerName = Container.ProducerName;
            keyedBatch.MessageMetadata.PublishTime = (ulong)DateTimeHelper.CurrentUnixTimeMillis();
			keyedBatch.MessageMetadata.NumMessagesInBatch = numMessagesInBatch;

			if (CurrentTxnidMostBits != -1)
			{
				keyedBatch.MessageMetadata.TxnidMostBits = (ulong)CurrentTxnidMostBits;
			}
			if (CurrentTxnidLeastBits != -1)
			{
				keyedBatch.MessageMetadata.TxnidLeastBits = (ulong)CurrentTxnidLeastBits;
			}
			var cmd = Commands.NewSend(Container.ProducerId, keyedBatch.SequenceId, numMessagesInBatch, keyedBatch.MessageMetadata, new ReadOnlySequence<byte>(encryptedPayload));

			var op = ProducerActor<T>.OpSendMsg<T>.Create((List<Message<T>>)keyedBatch.Messages, cmd, keyedBatch.SequenceId);

			op.NumMessagesInBatch = numMessagesInBatch;
			op.BatchSizeByte = currentBatchSizeBytes;
			return op;
		}

		public override IList<ProducerActor<T>.OpSendMsg<T>> CreateOpSendMsgs()
		{
			var result = new List<ProducerActor<T>.OpSendMsg<T>>();
			var list = new List<KeyedBatch>(_batches.Values);
			list.Sort(((o1, o2) => o1.SequenceId.CompareTo(o2.SequenceId)));
			foreach (var keyedBatch in list)
			{
				var op = CreateOpSendMsg(keyedBatch);
				if (op != null)
				{
					result.Add(op);
				}
			}
			return result;
		}

		public override bool HasSameSchema(Message<T> msg)
		{
			var key = GetKey(msg);
			var part = _batches[key];
			if (part == null || part.Messages.Count == 0)
			{
				return true;
			}
			if (!(part.MessageMetadata.ShouldSerializeSchemaVersion()))
			{
				return msg.SchemaVersion == null;
			}
			return Equals(msg.SchemaVersion, part.MessageMetadata.SchemaVersion);
		}

		private string GetKey(Message<T> msg)
		{
			if (msg.HasOrderingKey())
			{
				return Convert.ToBase64String(msg.OrderingKey);
			}
			return msg.Key;
		}

		public class KeyedBatch
		{
			internal MessageMetadata MessageMetadata = new MessageMetadata();
			// sequence id for this batch which will be persisted as a single entry by broker
			internal long SequenceId = -1;
			internal List<byte> BatchedMessageMetadataAndPayload = new List<byte>();
			internal IList<Message<T>> Messages = new List<Message<T>>();
			internal Action<object, Exception> PreviousCallback = null;
			internal CompressionType CompressionType;
			internal CompressionCodec Compressor;
			internal int MaxBatchSize;
			internal string TopicName;
			internal string ProducerName;

			// keep track of callbacks for individual messages being published in a batch
			internal Action<object, Exception> FirstCallback;

			public virtual byte[] CompressedBatchMetadataAndPayload
			{
				get
				{
					var stream = Helpers.Serializer.MemoryManager.GetStream();
					var messageWriter = new BinaryWriter(stream);
					foreach (var msg in Messages)
					{
						var msgMetadata = msg.Metadata.OriginalMetadata;
						Serializer.SerializeWithLengthPrefix(stream, Commands.SingleMessageMetadat(msgMetadata, (int)msg.Data.Length, msg.SequenceId), PrefixStyle.Fixed32BigEndian);
						messageWriter.Write(msg.Data.ToArray());
					}
					var batchedMessageMetadataAndPayload = stream.ToArray();
					var uncompressedSize = batchedMessageMetadataAndPayload.Length;
					var compressedPayload = Compressor.Encode(batchedMessageMetadataAndPayload);
					BatchedMessageMetadataAndPayload = null;
					if (CompressionType != CompressionType.None)
					{
						MessageMetadata.Compression = CompressionType;
						MessageMetadata.UncompressedSize = (uint)uncompressedSize;
					}
    
					// Update the current max batch Size using the uncompressed Size, which is what we need in any case to
					// accumulate the batch content
					MaxBatchSize = Math.Max(MaxBatchSize, uncompressedSize);
					return compressedPayload;
				}
			}

			public virtual void AddMsg(Message<T> msg, Action<object, Exception> callback)
			{
				if (Messages.Count == 0)
				{
					SequenceId = Commands.InitBatchMessageMetadata(MessageMetadata);
					BatchedMessageMetadataAndPayload.AddRange(msg.Data.ToArray());
					FirstCallback = callback;
				}
;
                PreviousCallback = callback;
				Messages.Add(msg);
			}

			public virtual void Discard(Exception ex)
			{
				try
                {
                    // Need to protect ourselves from any exception being thrown in the future handler from the application
                    FirstCallback(null, ex);
                }
				catch (Exception t)
				{
					_log.Warning($"[{TopicName}] [{ProducerName}] Got exception while completing the callback for msg {SequenceId}:{t}");
				}
				Clear();
			}

			public virtual void Clear()
			{
				Messages =  new List<Message<T>>();
				FirstCallback = null;
				PreviousCallback = null;
				MessageMetadata = new MessageMetadata();
				SequenceId = -1;
				BatchedMessageMetadataAndPayload = null;
			}
		}

	}

}