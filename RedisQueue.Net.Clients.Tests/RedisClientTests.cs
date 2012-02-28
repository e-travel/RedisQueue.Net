using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using RedisQueue.Net.Clients.Entities;
using ServiceStack.Redis;

namespace RedisQueue.Net.Clients.Tests
{
	[TestFixture]
	public class RedisClientTests
	{
		protected virtual Process RedisServer { get; set; }

		[TestFixtureSetUp]
		public void SetUp()
		{
			var proc = new ProcessStartInfo
			{
				FileName = "fixtures\\Redis.64bit\\redis-server.exe",
				Arguments = "redis.conf",
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				UseShellExecute = false,
				WorkingDirectory = "fixtures\\Redis.64bit\\"
			};

			RedisServer = Process.Start(proc);
			RedisServer.OutputDataReceived += (sender, data) => Console.WriteLine(data.Data);
			RedisServer.ErrorDataReceived += (sender, data) => Console.WriteLine("ERROR: " + data.Data);
		}

		[Test]
		public void TestCanEnqueueTaskInRedisAndReadItBack()
		{
			var client = new SimpleClient();
			var homemadeTask = new TaskMessage
			{
				Parameters = "params", 
				Queue = "TestQueue"
			};

			client.Enqueue(homemadeTask);
			var retrievedTask = client.Reserve("TestQueue");
			
			Assert.AreEqual(retrievedTask.Parameters, homemadeTask.Parameters);
			Assert.AreEqual(retrievedTask.Queue, homemadeTask.Queue);
		}

		[Test]
		public void TestEnqueueAndDelete100Tasks()
		{
			var client = new SimpleClient();
			for(var i = 0; i < 100; i++)
			{
				client.Enqueue(new TaskMessage
				{
					Parameters = "params",
					Queue = "TestQueue"
				});
			}

			Assert.AreEqual(client.AllTasks("TestQueue").Count, 100);

			var deletedTasks = 0;
			while(client.AllTasks("TestQueue").Count > 0)
			{
				client.RemoveTask(client.AllTasks("TestQueue")[0]);
				deletedTasks++;
			}

			Assert.AreEqual(deletedTasks, 100);
		}

		[Test]
		public void TestThrowsExceptionOnRedisDeath()
		{
			var client = new SimpleClient();
			RedisServer.Kill();

			Assert.Throws<RedisException>(() => client.Enqueue(new TaskMessage
			{
				Parameters = "params",
				Queue = "TestQueue"
			}));

			RedisServer.Start();
		}

		[Test]
		public void TestSendMessageThroughRedis()
		{
			var receiver = new SimpleClient();
			var subscription = receiver.GetSubscription();

			var messageReceived = false;
			subscription.OnMessage = (queue, message) =>
			{
			    messageReceived = true;
				Assert.AreEqual(queue, "TestQueue");
				Assert.AreEqual(message, "Test Message");
			};
			Action a = () => subscription.SubscribeToChannels(new QueueName("TestQueue").ChannelName);
			a.BeginInvoke(null, null);

			var sender = new SimpleClient();
			sender.SendMessage("Test Message", "TestQueue");

			Thread.Sleep(1000);

			Assert.IsTrue(messageReceived);
		}

		[TestFixtureTearDown]
		public void TearDown()
		{
			RedisServer.Kill();
			RedisServer.Dispose();
		}
	}
}