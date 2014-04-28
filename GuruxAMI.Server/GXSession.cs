using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace GuruxAMI.Server
{
    internal class GXSession
    {
        /// <summary>
        /// Session Guid.
        /// </summary>
        public Guid Session;

        public ManualResetEvent Received = new ManualResetEvent(false);

        /// <summary>
        /// Registered clients.
        /// </summary>
        public List<GXEvent> NotifyClients = new List<GXEvent>();

        public GXSession(Guid guid)
        {
            Session = guid;
        }
    }
}
