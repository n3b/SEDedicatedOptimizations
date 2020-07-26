using System;
using System.Timers;

namespace SEClientFixes.Util
{
    /// <summary>
    /// Provides Debounce() and Throttle() methods.
    /// Use these methods to ensure that events aren't handled too frequently.
    /// 
    /// Throttle() ensures that events are throttled by the interval specified.
    /// Only the last event in the interval sequence of events fires.
    /// 
    /// Debounce() fires an event only after the specified interval has passed
    /// in which no other pending event has fired. Only the last event in the
    /// sequence is fired.
    /// </summary>
    public class TimerUtil
    {
        private Timer timer;
        private DateTime timerStarted { get; set; } = DateTime.UtcNow.AddYears(-1);
        private Action<object> lastAction;
        private object _lock = new object();

        /// <summary>
        /// Debounce an event by resetting the event timeout every time the event is 
        /// fired. The behavior is that the Action passed is fired only after events
        /// stop firing for the given timeout period.
        /// 
        /// Use Debounce when you want events to fire only after events stop firing
        /// after the given interval timeout period.
        /// 
        /// Wrap the logic you would normally use in your event code into
        /// the  Action you pass to this method to debounce the event.
        /// Example: https://gist.github.com/RickStrahl/0519b678f3294e27891f4d4f0608519a
        /// </summary>
        /// <param name="interval">Timeout in Milliseconds</param>
        /// <param name="action">Action<object> to fire when debounced event fires</object></param>
        /// <param name="param">optional parameter</param>    
        public void Debounce(int interval, Action<object> action, object param = null)
        {
            lock (_lock)
            {
                timer?.Stop();
                timer = null;
                timer = new Timer(interval);
                timer.Elapsed += (s, e) =>
                {
                    lock (_lock)
                    {
                        if (timer == null) return;
                        timer.Stop();
                        timer = null;
                    }

                    action?.Invoke(param);
                };

                timer.Start();
            }
        }

        /// <summary>
        /// This method throttles events by allowing only 1 event to fire for the given
        /// timeout period. Only the last event fired is handled - all others are ignored.
        /// Throttle will fire events every timeout ms even if additional events are pending.
        /// 
        /// Use Throttle where you need to ensure that events fire at given intervals.
        /// </summary>
        /// <param name="interval">Timeout in Milliseconds</param>
        /// <param name="action">Action<object> to fire when debounced event fires</object></param>
        /// <param name="param">optional parameter</param>
        public bool Throttle(int interval, Action<object> action, object param = null)
        {
            lock (_lock)
            {
                var curTime = DateTime.UtcNow;
                lastAction = action;
                if (timer != null) return false;

                timer = new Timer(interval);
                timer.Elapsed += (s, e) =>
                {
                    Action<object> _action;
                    lock (_lock)
                    {
                        if (timer == null) return;
                        _action = lastAction;
                        lastAction = null;
                        timer.Stop();
                        timer = null;
                    }

                    _action?.Invoke(param);
                };

                timer.Start();
                timerStarted = curTime;
                return true;
            }
        }
    }
}