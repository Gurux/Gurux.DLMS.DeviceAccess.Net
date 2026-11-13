// --------------------------------------------------------------------------
// Gurux Ltd
//
// Copyright (c) Gurux Ltd.
//
// This file is part of Gurux Device Framework.
//
// Commit: $Format:%H$
// Date:   $Format:%cI$
//
// This software is available under either of the following licenses:
//
// 1. GNU General Public License, version 2 only (GPL-2.0-only).
//    See the LICENSE file included with this distribution.
//
// 2. A commercial license granted by Gurux Ltd under a separate written
//    agreement. Use under this option requires a valid commercial license
//    and is governed by the terms of that agreement.
//
// If you have not obtained a commercial license from Gurux Ltd,
// use of this software is governed by GPL-2.0-only.
//
// Commercial licensing: https://www.gurux.fi/
//
// SPDX-License-Identifier: GPL-2.0-only OR LicenseRef-Gurux-Commercial
// --------------------------------------------------------------------------

using System;

namespace Gurux.DLMS.DeviceAccess
{
    /// <summary>A transmitted request or received response and its capture time.</summary>
    public sealed class GXPacketTrace
    {
        /// <summary>Gets whether the bytes were transmitted to the meter rather than received from it.</summary>
        public bool Sent { get; }
        /// <summary>Gets the isolated copy of the captured request or response bytes.</summary>
        /// <remarks>Changing this array does not modify the reader's protocol buffers.</remarks>
        public byte[] Data { get; }
        /// <summary>Gets the UTC time when this trace report was constructed.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>Copies protocol bytes and records the capture time before invoking the host callback.</summary>
        /// <param name="sent">True for transmitted bytes; false for received bytes.</param>
        /// <param name="data">The protocol bytes to copy into the report.</param>
        /// <exception cref="NullReferenceException"><paramref name="data"/> is null.</exception>
        internal GXPacketTrace(bool sent, byte[] data)
        {
            Sent = sent;
            Data = (byte[])data.Clone();
            Timestamp = DateTimeOffset.UtcNow;
        }
    }
}
