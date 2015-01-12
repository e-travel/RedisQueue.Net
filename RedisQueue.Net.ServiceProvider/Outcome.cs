namespace RedisQueue.Net.ServiceProvider
{
	public enum Outcome
	{
		NotStarted,
		Success,
		Failure,
		CriticalFailure,
        Defer
	}
}