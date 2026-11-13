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

namespace Gurux.DLMS.DeviceAccess
{
    /// <summary>Identifies the firmware-update phase reported to the caller.</summary>
    public enum ImageUpdateStage
    {
        /// <summary>The image identity and transfer state are being prepared before sending missing blocks.</summary>
        Initiating,
        /// <summary>An image block has been acknowledged, including a retransmitted missing block.</summary>
        Transferring,
        /// <summary>The library is requesting image verification and waiting for the meter's result.</summary>
        Verifying,
        /// <summary>The library is activating the verified image and waiting for confirmation, reconnecting when required.</summary>
        Activating,
        /// <summary>All requested phases have completed; transfer-only updates do not imply that firmware was activated.</summary>
        Completed
    }
}
