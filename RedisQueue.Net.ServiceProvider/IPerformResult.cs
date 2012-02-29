namespace RedisQueue.Net.ServiceProvider
{
	public interface IPerformResult
	{
		Outcome Outcome { get; set; }
		string Reason { get; set; }
		object Data { get; set; }
	}
}