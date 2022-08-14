using System;
using System.Collections.Generic;
using System.Text;

namespace Keryhe.Messaging.Polling.Delay
{
    public interface IDelay
    {
        void Wait();
        void Cancel();
        void Reset();
    }
}