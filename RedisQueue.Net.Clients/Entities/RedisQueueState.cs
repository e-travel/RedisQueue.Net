namespace RedisQueue.Net.Clients.Entities
{
	/// <summary>
	/// Represents the internal state of the RedisQueue client.
	/// </summary>
	public enum RedisQueueState
	{
		/// <summary>
		/// Client can reserve and enqueue tasks.
		/// </summary>
		Ready = 0,

		/// <summary>
		/// Client can only enqueue tasks, having already reserved a single task.
		/// </summary>
		TaskReserved = 1
	}
}