﻿using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SignalRBaseHubClientLib;
using RemoteInterfaces;

namespace SignalRClient
{
    class ProgramSignalRClient
    {
        private const string Url    = "http://localhost:15000/hub/a";
        private const string UrlTls = "https://localhost:15001/hub/a";

        private static async Task Main(string[] args)
        {
            var url = args.Length > 0 && args[0].ToLower() == "tls" ? UrlTls : Url;

            using ILoggerFactory loggerFactory =
                LoggerFactory.Create(builder =>
                    builder.AddSimpleConsole(options =>
                    {
                        options.IncludeScopes = false;
                        options.SingleLine = true;
                        options.TimestampFormat = "hh:mm:ss ";
                    }));
            var logger = loggerFactory.CreateLogger<ProgramSignalRClient>();

            logger.LogInformation("SignalRClientTest started");

            // Create hub client and connect to server
            using var hubClient = await new HubClient(url, loggerFactory, $"Client-{Guid.NewGuid()}", 
                        null,
                        (logger, isOneWay, request, result, duration, exception) => 
                        {
                            var message = $"Method '{request.InterfaceName}.{request.MethodName}()', isOneWay = {isOneWay}, requestId = {request.Id} ";
                            if (exception == null)
                                logger?.LogInformation($"{message}OK, duration = {duration.TotalMilliseconds} ms");
                            else
                                logger?.LogError($"{message}", exception);
                        })
                    .RegisterInterface<IRemoteCall1>()
                    .RegisterInterface<IRemoteCall2>()
                    .RegisterInterface<IRemoteCall3>()
                    .StartConnectionAsync(retryIntervalMs: 1000, numOfAttempts: 15);

            #region Streaming

            // Client subscribes for stream of Message objects providing appropriate handler
            hubClient.Subscribe<Message>(arg => logger.LogInformation($"Stream: {arg}"));

            #endregion Streaming

            AutoResetEvent ev = new(false);

            var args1 = new Arg1[]
                {
                    new Arg1 { Id = "0", Arg2Props = new() { new() { Id = "0.0" }, new() { Id = "0.1" } } },
                    new Arg1 { Id = "1", Arg2Props = new() { new() { Id = "1.0" }, new() { Id = "1.1" } } }
                };

            _ = Task.Run(async () =>
            {
                // Client provides handler for server's call of method ReceiveMessage
                hubClient.Connection.On("ReceiveMessage", (string s0, string s1) => 
                    logger.LogInformation($"ReceiveMessage: {s0} {s1}"));

                while (!ev.WaitOne(3000))
                {
                    #region Rpc

                    var str = await hubClient.RpcAsync("IRemoteCall1", "Echo", " some text");

                    var ret3_1 = (Ret3)await hubClient.RpcAsync("IRemoteCall3", "GetIdAndParam");
                    var ret3_2 = (Ret3)await hubClient.RpcAsync("IRemoteCall3", "GetIdAndParam");

                    //TEST
                    if (ret3_1.Id != ret3_2.Id || ret3_1.Param != ret3_2.Param)
                        throw new Exception("TEST failes - wrong Ret3");

                    var result42 = await hubClient.RpcAsync("IRemoteCall1", "Simple");

                    var task1 = hubClient.RpcAsync("IRemoteCall1", "Foo", "theName", args1);
                    var task2 = hubClient.RpcAsync("IRemoteCall2", "Foo", "theName", args1);

                    hubClient.RpcOneWay("IRemoteCall1", "Foo", "theName", args1);
                    hubClient.RpcOneWay("IRemoteCall2", "Foo", "theName", args1);

                    var echo = await hubClient.RpcAsync("IRemoteCall1", "Echo", " my text");

                    hubClient.RpcOneWay("IRemoteCall2", "Foo", "theName", args1);

                    var result1 = (RetOuter[])await task1;
                    var result2 = (int)await task2;

                    //TEST
                    if (result1 == null || result1.Length != 2 || result1[1].Inners[3].Id != "1_11")
                        throw new Exception("TEST failes - wrong arguments");

                    var durationTicksFoo1 = await TimeWatch(hubClient.RpcAsync, "IRemoteCall1", "Foo", "theName", args1);
                    var durationTicksFoo2 = await TimeWatch(hubClient.RpcAsync, "IRemoteCall2", "Foo", "theName", args1);

                    var durationTicksEcho1 = await TimeWatch(hubClient.RpcAsync, "IRemoteCall1", "Echo", " some text");
                    var durationTicksEcho2 = await TimeWatch(hubClient.RpcAsync, "IRemoteCall2", "Echo", " some text");

                    var durationTicksFooOneWay1 = await TimeWatch(hubClient.RpcOneWay, "IRemoteCall1", "Foo", "theName", args1);
                    var durationTicksFooOneWay2 = await TimeWatch(hubClient.RpcOneWay, "IRemoteCall2", "Foo", "theName", args1);

                    var strFoo = ((float)durationTicksFoo1 / durationTicksFoo2).ToString("f1");
                    var strEcho = ((float)durationTicksEcho1 / durationTicksEcho2).ToString("f1");
                    logger.LogInformation($"durationTicksFoo1  (reflected call): {durationTicksFoo1}");
                    logger.LogInformation($"durationTicksFoo2  (direct call):    {durationTicksFoo2}, Ratio: {strFoo}");
                    logger.LogInformation($"durationTicksEcho1 (reflected call): {durationTicksEcho1}");
                    logger.LogInformation($"durationTicksEcho2 (direct call):    {durationTicksEcho2}, Ratio: {strEcho}");
                    logger.LogInformation($"durationTicksFooOneWay1:             {durationTicksFooOneWay1}");
                    logger.LogInformation($"durationTicksFooOneWay2:             {durationTicksFooOneWay2}");

                    #endregion // Rpc

                    #region InvokeAsync

                    // Client calls server's method ProcessMessage
                    var jarr = (JArray)await hubClient.InvokeAsync("ProcessMessage",
                    new[]
                    {
                        new Message { ClientId = ".NETCoreClient", Data = 91, Args = new Arg1[]
                            {
                                new() { Id = "0", Arg2Props = new() { new() { Id = "0.0" }, new() { Id = "0.1" }, } },
                                new() { Id = "1", Arg2Props = new() { new() { Id = "1.0" }, new() { Id = "1.1" }, } },
                            }
                        },
                        new Message { ClientId = ".NETCoreClient", Data = 92, Args = new Arg1[]
                            {
                                new() { Id = "0", Arg2Props = new() { new() { Id = "0.0" }, new() { Id = "0.1" }, } },
                                new() { Id = "1", Arg2Props = new() { new() { Id = "1.0" }, new() { Id = "1.1" }, } },
                            }
                        },
                    });

                    #endregion // InvokeAsync
                }
            });

            Console.WriteLine("Press any key to cancel...");
            Console.ReadKey();
            ev.Set();
            await hubClient.Cancel();

            Console.WriteLine("Press any key to quit...");
            Console.ReadKey();
        }

        static async Task<long> TimeWatch(Func<string, string, object[], Task<object>> func, string interfaceName, string methodName, params object[] args)
        {
            Stopwatch sw = new();
            sw.Start();
            var result = await func(interfaceName, methodName, args);
            sw.Stop();
            return sw.Elapsed.Ticks;
        }

        static async Task<long> TimeWatch(Action<string, string, object[]> action, string interfaceName, string methodName, params object[] args)
        {
            Stopwatch sw = new();
            sw.Start();
            action(interfaceName, methodName, args);
            sw.Stop();
            return sw.Elapsed.Ticks;
        }
    }
}
