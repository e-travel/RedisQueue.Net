using System;

namespace RedisQueue.Net.Clients.Exceptions
{
	/// <summary>
	/// Thrown by <c>RedisQueue.Reserve()</c> when the specified queue is empty.
	/// </summary>
	public class QueueIsEmptyException : Exception
	{
		public QueueIsEmptyException(string message) : base (message) {}
	}
}