﻿using Akka.Actor;
using Akka.Configuration;
using NLog;
using SharpPulsar.Configuration;
using SharpPulsar.Messages.Client;
using SharpPulsar.Transaction;
using SharpPulsar.User;
using SharpPulsar.User.Events;
using System;
using System.Threading.Tasks;
using SharpPulsar.Sql.Client;
using SharpPulsar.Sql.Public;
using AskResponse = SharpPulsar.Messages.Consumer.AskResponse;

namespace SharpPulsar
{
    public sealed class PulsarSystem
    {
        private static PulsarSystem _instance;
        private static readonly object Lock = new object();
        private static ActorSystem _actorSystem;
        private readonly ClientConfigurationData _conf;
        private readonly IActorRef _cnxPool;
        private readonly IActorRef _client;
        private readonly IActorRef _tcClient;
        private readonly IActorRef _lookup;
        private readonly IActorRef _generator;
        private readonly Action _logSetup = () => 
        {
            var nlog = new NLog.Config.LoggingConfiguration();
            var logfile = new NLog.Targets
                .FileTarget("logFile")
            {
                FileName = "logs.log",
                Layout = "[${longdate}] [${logger}] ${level:uppercase=true}] : ${event-properties:actorPath} ${message} ${exception:format=tostring}",
                ArchiveEvery = NLog.Targets.FileArchivePeriod.Hour,
                ArchiveNumbering = NLog.Targets.ArchiveNumberingMode.DateAndSequence
            };
            nlog.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);
            LogManager.Configuration = nlog;
        };
        public static PulsarSystem GetInstance(ActorSystem actorSystem, PulsarClientConfigBuilder conf)
        {
            if (_instance == null)
            {
                lock (Lock)
                {
                    if (_instance == null)
                    {
                        _instance = new PulsarSystem(actorSystem, conf);
                    }
                }
            }
            return _instance;
        }
        public static PulsarSystem GetInstance(PulsarClientConfigBuilder conf, Action logSetup = null, Config config = null)
        {
            if (_instance == null)
            {
                lock (Lock)
                {
                    if (_instance == null)
                    {
                        _instance = new PulsarSystem(conf, logSetup, config);
                    }
                }
            }
            return _instance;
        }
        private PulsarSystem(PulsarClientConfigBuilder confBuilder, Action logSetup, Config confg)
        {

            _conf = confBuilder.ClientConfigurationData;
            var conf = _conf;
            var logging = logSetup ?? _logSetup;
            logging();
            _conf = conf;
            var config = confg ?? ConfigurationFactory.ParseString(@"
            akka
            {
                loglevel = DEBUG
			    log-config-on-start = on 
                loggers=[""Akka.Logger.NLog.NLogLogger, Akka.Logger.NLog""]
			    actor 
                {              
				      debug 
				      {
					      receive = on
					      autoreceive = on
					      lifecycle = on
					      event-stream = on
					      unhandled = on
				      }  
			    }
                coordinated-shutdown
                {
                    exit-clr = on
                }
            }"
            );
            _actorSystem = ActorSystem.Create("Pulsar", config);
            
            _cnxPool = _actorSystem.ActorOf(ConnectionPool.Prop(conf), "ConnectionPool");
            _generator = _actorSystem.ActorOf(IdGeneratorActor.Prop(), "IdGenerator");
            _lookup = _actorSystem.ActorOf(BinaryProtoLookupService.Prop(_cnxPool, _generator, conf.ServiceUrl, conf.ListenerName, conf.UseTls, conf.MaxLookupRequest, conf.OperationTimeout), "BinaryProtoLookupService");

            if (conf.EnableTransaction)
            {
                _tcClient = _actorSystem.ActorOf(TransactionCoordinatorClient.Prop(_lookup, _cnxPool, _generator, conf));
                var cos = _tcClient.Ask<AskResponse>("Start").GetAwaiter().GetResult();
                if (cos.Failed)
                    throw cos.Exception;

                if ((int)cos.Data <= 0)
                    throw new Exception($"Tranaction Coordinator has '{cos}' transaction handler");
            } 
            _client = _actorSystem.ActorOf(Props.Create(()=> new PulsarClientActor(conf,  _cnxPool, _tcClient, _lookup, _generator)), "PulsarClient");
            _lookup.Tell(new SetClient(_client));

        }
        private PulsarSystem(ActorSystem actorSystem, PulsarClientConfigBuilder confBuilder)
        {
            _conf = confBuilder.ClientConfigurationData;
            var conf = _conf;
            _actorSystem = actorSystem;
            _conf = conf;
            _cnxPool = _actorSystem.ActorOf(ConnectionPool.Prop(conf), "ConnectionPool");
            _generator = _actorSystem.ActorOf(IdGeneratorActor.Prop(), "IdGenerator");
            _lookup = _actorSystem.ActorOf(BinaryProtoLookupService.Prop(_cnxPool, _generator, conf.ServiceUrl, conf.ListenerName, conf.UseTls, conf.MaxLookupRequest, conf.OperationTimeout), "BinaryProtoLookupService");

            if (conf.EnableTransaction)
            {
                _tcClient = _actorSystem.ActorOf(TransactionCoordinatorClient.Prop(_lookup, _cnxPool, _generator, conf));
                var cos = _tcClient.Ask<AskResponse>("Start").GetAwaiter().GetResult();
                if (cos.Failed)
                    throw cos.Exception;

                if ((int)cos.Data <= 0)
                    throw new Exception($"Tranaction Coordinator has '{cos}' transaction handler");
            }

            _client = _actorSystem.ActorOf(Props.Create<PulsarClientActor>(conf, _cnxPool, _tcClient, _lookup, _generator), "PulsarClient");
            _lookup.Tell(new SetClient(_client));
        }
        public PulsarClient NewClient() 
        {
            return new PulsarClient(_client, _lookup, _cnxPool, _generator, _conf, _actorSystem, _tcClient);
        }
        public EventSourceBuilder EventSource(string tenant, string @namespace, string topic, long fromMessageId, long toMessageId, string brokerWebServiceUrl) 
        {
            return new EventSourceBuilder(_actorSystem, _client, _lookup, _cnxPool, _generator, tenant, @namespace, topic, fromMessageId, toMessageId, brokerWebServiceUrl);
        }

        public static SqlInstance Sql(ClientOptions options) 
        {
            return new SqlInstance(_actorSystem, options);
        }
        public static SqlInstance Sql(ActorSystem actorSystem, ClientOptions options)
        {
            if (actorSystem == null)
                throw new Exception("ActorSystem can not be null");

            return new SqlInstance(actorSystem, options);
        }
        public static LiveSqlInstance LiveSql(ClientOptions options, string topic, TimeSpan interval, DateTime startAtPublishTime) 
        {
            return new LiveSqlInstance(_actorSystem, options, topic, interval, startAtPublishTime);
        }
        public static LiveSqlInstance LiveSql(ActorSystem actorSystem, ClientOptions options, string topic, TimeSpan interval, DateTime startAtPublishTime)
        {
            if (actorSystem == null)
                throw new Exception("ActorSystem can not be null");
            return new LiveSqlInstance(_actorSystem, options, topic, interval, startAtPublishTime);
        }

        public ActorSystem System => _actorSystem;
        public async Task Shutdown()
        {
            await _actorSystem.Terminate();
        }
    }
}
