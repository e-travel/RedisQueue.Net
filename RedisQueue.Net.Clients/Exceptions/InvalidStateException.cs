using System;

namespace RedisQueue.Net.Clients.Exceptions
{
	/// <summary>
	/// An invalid state has been reached by the client. Internal error that indicates 
	/// something unexpected happened in the innards of the client.
	/// </summary>
	public class InvalidStateException : Exception
	{
		public InvalidStateException(string message) : base(message) {}
	}
}