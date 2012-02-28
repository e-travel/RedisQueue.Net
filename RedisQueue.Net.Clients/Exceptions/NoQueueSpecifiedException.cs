using System;

namespace RedisQueue.Net.Clients.Exceptions
{
	/// <summary>
	/// An operation requesting a queue was not provided with one. This is usually
	/// thrown by the RedisQueue client during the Reserve or Enqueue operations.
	/// </summary>
	public class NoQueueSpecifiedException : Exception
	{
		public NoQueueSpecifiedException(string message) : base(message) {}
	}
}