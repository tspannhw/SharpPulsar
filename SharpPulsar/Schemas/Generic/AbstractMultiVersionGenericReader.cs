﻿
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
namespace SharpPulsar.Schemas.Generic
{
    using SharpPulsar.Schemas.Reader;
    using SharpPulsar.Interfaces.ISchema;

    /// <summary>
    /// The abstract class of multi version generic reader.
    /// </summary>
    public abstract class AbstractMultiVersionGenericReader : AbstractMultiVersionAvroBaseReader<IGenericRecord>
    {

        // the flag controls whether to use the provided schema as reader schema
        // to decode the messages. In `AUTO_CONSUME` mode, setting this flag to `false`
        // allows decoding the messages using the schema associated with the messages.
        protected internal readonly bool useProvidedSchemaAsReaderSchema;

        protected internal AbstractMultiVersionGenericReader(bool useProvidedSchemaAsReaderSchema, ISchemaReader<IGenericRecord> providerSchemaReader, Avro.Schema readerSchema) : base(providerSchemaReader, readerSchema)
        {
            this.useProvidedSchemaAsReaderSchema = useProvidedSchemaAsReaderSchema;
        }

    }
}
