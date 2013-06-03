using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace GuruxAMI.Server
{
    internal class GXSession
    {
        public Guid ListenerGuid;

        public ManualResetEvent Received = new ManualResetEvent(false);

        /// <summary>
        /// Registered clients.
        /// </summary>
        public List<GXEvent> NotifyClients = new List<GXEvent>();

        public GXSession(Guid guid)
        {
            ListenerGuid = guid;
        }
    }
}
