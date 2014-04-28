using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuruxAMI.Common.Messages;

namespace GuruxAMI.Server
{
    internal class GXEvent
    {
        /// <summary>
        /// Reveiced data of the event.
        /// </summary>
        public List<GXEventsItem> Rows;

        /// <summary>
        /// 
        /// </summary>
        public ulong Mask
        {
            get;
            internal set;
        }

        /// <summary>
        /// User ID.
        /// </summary>
        public long UserID
        {
            get;
            internal set;
        }

        /// <summary>
        /// User ID.
        /// </summary>
        public bool SuperAdmin
        {
            get;
            internal set;
        }

        /// <summary>
        /// DataCollector Guid
        /// </summary>
        public Guid DataCollectorGuid
        {
            get;
            set;
        }

        /// <summary>
        /// Instance Guid
        /// </summary>
        public Guid Instance
        {
            get;
            set;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public GXEvent(long userId, bool superAdmin, Guid dataCollectorGuid, Guid instance, ulong mask)
        {
            Instance = instance;
            Mask = mask;
            DataCollectorGuid = dataCollectorGuid;
            SuperAdmin = superAdmin;
            UserID = userId;
            Rows = new List<GXEventsItem>();
        }
    }
}
