using System;

namespace RedisQueue.Net.Clients.Exceptions
{
	/// <summary>
	/// Thrown by <c>TaskMessage</c>. Signifies that the name we specified for the
	/// task message is not valid. Valid names conform to this regular expression: ^[a-zA-Z]{1}[\w_]+$
	/// </summary>
	public class InvalidQueueNameException : Exception
	{
		public InvalidQueueNameException(string message) : base (message) {}
	}
}