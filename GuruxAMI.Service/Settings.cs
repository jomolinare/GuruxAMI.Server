//
// --------------------------------------------------------------------------
//  Gurux Ltd
// 
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License 
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of 
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2. 
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Data;
using ServiceStack.OrmLite;
using ServiceStack.DesignPatterns.Model;

namespace GuruxAMI.Service
{
    /// <summary>
    /// Id for Device and device group is unique.
    /// We do not want to find new ID from the table.
    /// For this reason new ID is retreaved from here.
    /// </summary>
    [Serializable, Alias("Settings")]
	internal class GXAmiSettings : IHasId<ulong>
	{	
        /// <summary>
        /// Setting type.
        /// </summary>
        [DataMember()]
        [Alias("ID"), AutoIncrement, Index(Unique = true)]
        public ulong Id
		{
			get;
			set;
		}

        /// <summary>
        /// GuruxAMI setting value.
        /// </summary>
        [DataMember()]
        public string Value
        {
            get;
            set;
        }

        /// <summary>
        /// GuruxAMI setting Name.
        /// </summary>
        [DataMember()]
        public string Name
        {
            get;
            set;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public GXAmiSettings()
        {
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public GXAmiSettings(string name, string value)
        {
            this.Name = name;
            this.Value = value;
        }

        /// <summary>
        /// Get next device or device group ID.
        /// </summary>
        /// <param name="Db"></param>
        /// <returns></returns>
        public static ulong GetNewDeviceID(IDbConnection Db)
        {
            //Update ID. This causes that row is locked and others can't change it.
            List<GXAmiSettings> list = Db.Select<GXAmiSettings>(q => q.Name == "DeviceID");
            if (list.Count == 1)
            {
                Db.UpdateOnly(list[0], p => p.Value, p => p.Value == list[0].Value);
            }
            else
            {
                throw new Exception("Settings is corrupted. Invalid DeviceID.");
            }
            ulong value = 65536;
            ulong tmp = Convert.ToUInt64(list[0].Value);
            value += tmp;
            list[0].Value = value.ToString();
            Db.Update(list[0], p => p.Id == list[0].Id);
            return value;                
        }
	}
}
