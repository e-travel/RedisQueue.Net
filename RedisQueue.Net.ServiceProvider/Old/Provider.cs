using System;
using System.Threading;
using RedisQueue.Net.Clients.Entities;
using ServiceStack.Logging;

namespace RedisQueue.Net.ServiceProvider.Old
{
	public class Provider : ServiceWrapper.ServiceProvider
	{
        private readonly QueueMonitor _monitor;
        private readonly QueueSubscriber _subscriber;
	    private readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof (ServiceWrapper.ServiceWrapper));

	    public Provider()
	    {
            this._monitor = new QueueMonitor();
            this._subscriber = new QueueSubscriber()
            {
                MessageReceived = new Action<string>(this.MessageReceived)
            };
	    }

        private void MessageReceived(string message)
        {
            _log.InfoFormat("Message received: {0}", (object)message);
            bool flag = true;
            if (!message.Equals(((object)QueueSystemMessages.TaskAvailable).ToString(), StringComparison.OrdinalIgnoreCase))
            {
                flag = false;
                _log.Warn((object)"Unrecognized message. Will ignore.");
            }
            if (!this._monitor.Waiting)
            {
                flag = false;
                _log.Warn((object)"Monitor is busy. Will set a flag for later.");
            }
            if (!flag)
                return;
            _log.Debug((object)"Monitor pulse.");
            lock (this._monitor)
                Monitor.Pulse((object)this._monitor);
        }

	    public override void Start(string[] args)
		{
            this._subscriber.Start((Action)(() => this._monitor.Start()));
		}

		public override void Stop()
		{
            this._subscriber.Stop((Action)(() => this._monitor.Stop()));
		}

	    public override void Continue()
	    {
            this._subscriber.Start((Action)(() => this._monitor.Start()));
	    }

	    public override void Pause()
	    {
            this._subscriber.Stop((Action)(() => this._monitor.Stop()));
	    }

	    public override void Shutdown()
	    {
            this._subscriber.Stop((Action)(() => this._monitor.Stop()));
	    }
	}
}