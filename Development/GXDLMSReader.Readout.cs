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

using Gurux.Common;
using Gurux.DLMS.Enums;
using Gurux.DLMS.Objects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using Resources = Gurux.DLMS.DeviceAccess.Properties.Resources;
using System.Text;
using Task = System.Threading.Tasks.Task;

namespace Gurux.DLMS.DeviceAccess
{
    public partial class GXDLMSReader
    {

        /// <summary>
        /// Connects, reads the association and available values, optionally saves the association view to XML, and closes the connection.
        /// </summary>
        /// <param name="outputFilePath">XML file path used to load cached object definitions and save the association view, optionally including attribute values according to settings, or null to disable file access.</param>
        /// <param name="settings">Settings for the final XML export, or null to save only the association view with Values set to false. Set Values to true to include read attribute values. These settings do not affect the intermediate association cache.</param>
        /// <remarks>Individual read failures may be skipped. XML save failures propagate to the caller; the connection is closed even if reading or saving fails.</remarks>
        /// <exception cref="IOException">The XML output cannot be written.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to the XML output path is denied.</exception>
        public void ReadAll(string? outputFilePath, GXXmlWriterSettings? settings = null) => ReadAllAsync(outputFilePath, settings).GetAwaiter().GetResult();
        /// <summary>
        /// Connects, reads the association and available values, optionally saves the association view to XML, and closes the connection.
        /// </summary>
        /// <param name="outputFilePath">XML file path used to load cached object definitions and save the association view, optionally including attribute values according to settings, or null to disable file access.</param>
        /// <param name="settings">Settings for the final XML export, or null to save only the association view with Values set to false. Set Values to true to include read attribute values. These settings do not affect the intermediate association cache.</param>
        /// <remarks>Individual read failures may be skipped. XML save failures propagate to the caller; the connection is closed even if reading or saving fails.</remarks>
        /// <exception cref="IOException">The XML output cannot be written.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to the XML output path is denied.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task ReadAllAsync(string? outputFilePath, GXXmlWriterSettings? settings = null)
        {
            try
            {
                await InitializeConnectionAsync().ConfigureAwait(false);
                if ((await GetAssociationViewAsync(outputFilePath).ConfigureAwait(false)))
                {
                    await GetScalersAndUnitsAsync().ConfigureAwait(false);
                    await GetProfileGenericColumnsAsync().ConfigureAwait(false);
                }

                await GetReadOutAsync().ConfigureAwait(false);
                if (outputFilePath != null)
                {
                    Client.Objects.Save(outputFilePath, settings ?? new GXXmlWriterSettings { Values = false });
                }
            }
            finally
            {
                await CloseAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads register scaler and unit attributes using negotiated bulk services where possible.
        /// </summary>
        /// <remarks>Falls back to individual reads when multiple-reference support is unavailable; failures of individual reads are ignored.</remarks>
        public void GetScalersAndUnits() => GetScalersAndUnitsAsync().GetAwaiter().GetResult();
        /// <summary>
        /// Reads register scaler and unit attributes using negotiated bulk services where possible.
        /// </summary>
        /// <remarks>Falls back to individual reads when multiple-reference support is unavailable; failures of individual reads are ignored.</remarks>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task GetScalersAndUnitsAsync()
        {
            GXDLMSObjectCollection objs = Client.Objects.GetObjects(new ObjectType[] { ObjectType.Register, ObjectType.ExtendedRegister, ObjectType.DemandRegister });
            //If trace is info.
            if (Trace > TraceLevel.Warning)
            {
                Log(Resources.ReadingScalersAndUnits);
            }

            //Access services are available only for general protection.
            if ((Client.NegotiatedConformance & Conformance.Access) != 0 && (Client.Ciphering.Security == Security.None || (Client.NegotiatedConformance & Conformance.GeneralProtection) != 0))
            {
                List<GXDLMSAccessItem> list = new List<GXDLMSAccessItem>();
                foreach (GXDLMSObject it in objs)
                {
                    if ((it is GXDLMSRegister || it is GXDLMSExtendedRegister) && Client.CanRead(it, 3))
                    {
                        list.Add(new GXDLMSAccessItem(AccessServiceCommandType.Get, it, 3));
                    }
                    else if (it is GXDLMSDemandRegister && Client.CanRead(it, 4))
                    {
                        list.Add(new GXDLMSAccessItem(AccessServiceCommandType.Get, it, 4));
                    }
                }

                await ReadByAccessAsync(list).ConfigureAwait(false);
            }
            else if ((Client.NegotiatedConformance & Gurux.DLMS.Enums.Conformance.MultipleReferences) != 0)
            {
                List<KeyValuePair<GXDLMSObject, int>> list = new List<KeyValuePair<GXDLMSObject, int>>();
                foreach (GXDLMSObject it in objs)
                {
                    if ((it is GXDLMSRegister || it is GXDLMSExtendedRegister) && Client.CanRead(it, 3))
                    {
                        list.Add(new KeyValuePair<GXDLMSObject, int>(it, 3));
                    }

                    if (it is GXDLMSDemandRegister && Client.CanRead(it, 4))
                    {
                        list.Add(new KeyValuePair<GXDLMSObject, int>(it, 4));
                    }
                }

                if (list.Count != 0)
                {
                    try
                    {
                        await ReadListAsync(list).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        Client.NegotiatedConformance &= ~Gurux.DLMS.Enums.Conformance.MultipleReferences;
                    }
                }
            }

            if ((Client.NegotiatedConformance & Gurux.DLMS.Enums.Conformance.MultipleReferences) == 0)
            {
                //Read values one by one.
                foreach (GXDLMSObject it in objs)
                {
                    try
                    {
                        if (it is GXDLMSRegister && Client.CanRead(it, 3))
                        {
                            Log(it.Name);
                            await ReadAsync(it, 3).ConfigureAwait(false);
                        }

                        if (it is GXDLMSDemandRegister && Client.CanRead(it, 4))
                        {
                            Log(it.Name);
                            await ReadAsync(it, 4).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        //Actaric SL7000 can return error here. Continue reading.
                    }
                }
            }
        }

        /// <summary>
        /// Reads capture columns for each profile generic and traces their descriptions.
        /// </summary>
        /// <remarks>Failed profile reads are skipped so remaining profiles can be processed.</remarks>
        public void GetProfileGenericColumns() => GetProfileGenericColumnsAsync().GetAwaiter().GetResult();
        /// <summary>
        /// Reads capture columns for each profile generic and traces their descriptions.
        /// </summary>
        /// <remarks>Failed profile reads are skipped so remaining profiles can be processed.</remarks>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task GetProfileGenericColumnsAsync()
        {
            //Read Profile Generic columns first.
            foreach (GXDLMSProfileGeneric it in Client.Objects.GetObjects(ObjectType.ProfileGeneric))
            {
                try
                {
                    //If info.
                    if (Trace > TraceLevel.Warning)
                    {
                        Log(it.LogicalName);
                    }

                    if (Client.CanRead(it, 3))
                    {
                        await ReadAsync(it, 3).ConfigureAwait(false);
                    }

                    //If info.
                    if (Trace > TraceLevel.Warning)
                    {
                        GXDLMSObject[] cols = it.GetCaptureObject();
                        StringBuilder sb = new StringBuilder();
                        bool First = true;
                        foreach (GXDLMSObject col in cols)
                        {
                            if (!First)
                            {
                                sb.Append(" | ");
                            }

                            First = false;
                            sb.Append(col.Name);
                            sb.Append(" ");
                            sb.Append(col.Description);
                        }

                        Log(sb.ToString());
                    }
                }
                catch (Exception)
                {
                    //Continue reading.
                }
            }
        }

        /// <summary>
        /// Traces an attribute value when informational tracing is enabled, formatting byte arrays as hexadecimal.
        /// </summary>
        /// <param name="val">Value to format, including null, arrays and lists; null collection entries are rendered as empty fields.</param>
        /// <param name="pos">Attribute index included in the trace message.</param>
        public void ShowValue(object? val, int pos)
        {
            //If trace is info.
            if (Trace > TraceLevel.Warning)
            {
                //If data is array.
                if (val is byte[])
                {
                    val = GXCommon.ToHex((byte[])val, true);
                }
                else if (val is Array array)
                {
                    string str = "";
                    for (int pos2 = 0; pos2 != array.Length; ++pos2)
                    {
                        if (pos2 != 0)
                        {
                            str += ", ";
                        }

                        if (array.GetValue(pos2) is byte[] bytes)
                        {
                            str += GXCommon.ToHex(bytes, true);
                        }
                        else
                        {
                            str += array.GetValue(pos2)?.ToString();
                        }
                    }

                    val = str;
                }
                else if (val is System.Collections.IList list)
                {
                    string str = "[";
                    bool empty = true;
                    foreach (object? it2 in list)
                    {
                        if (!empty)
                        {
                            str += ", ";
                        }

                        empty = false;
                        if (it2 is byte[])
                        {
                            str += GXCommon.ToHex((byte[])it2, true);
                        }
                        else
                        {
                            str += it2?.ToString();
                        }
                    }

                    str += "]";
                    val = str;
                }

                Log(string.Format(CultureInfo.CurrentCulture, Resources.AttributeValue, pos, val));
            }
        }

        /// <summary>
        /// Reads profile entry counts and, when selective access is available, the first row and rows from today onward.
        /// </summary>
        /// <param name="items">Profile objects whose readable entry counts and selectively accessible rows are retrieved.</param>
        /// <remarks>Traces returned rows and logs failures of individual selective reads.</remarks>
        public void GetProfileGenerics(IEnumerable<GXDLMSProfileGeneric> items) => GetProfileGenericsAsync(items).GetAwaiter().GetResult();
        /// <summary>
        /// Reads profile entry counts and, when selective access is available, the first row and rows from today onward.
        /// </summary>
        /// <param name="items">Profile objects whose readable entry counts and selectively accessible rows are retrieved.</param>
        /// <remarks>Traces returned rows and logs failures of individual selective reads.</remarks>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task GetProfileGenericsAsync(IEnumerable<GXDLMSProfileGeneric> items)
        {
            //Find profile generics objects and read them.
            foreach (GXDLMSProfileGeneric it in items)
            {
                //If trace is info.
                if (Trace > TraceLevel.Warning)
                {
                    Log(string.Format(CultureInfo.CurrentCulture, Resources.ReadingObject, it.GetType().Name, it.Name, it.Description));
                }

                long entriesInUse = -1;
                if (Client.CanRead(it, 7))
                {
                    entriesInUse = Convert.ToInt64((await ReadAsync(it, 7).ConfigureAwait(false)));
                }

                long entries = -1;
                if (Client.CanRead(it, 8))
                {
                    entries = Convert.ToInt64((await ReadAsync(it, 8).ConfigureAwait(false)));
                }

                //If trace is info.
                if (Trace > TraceLevel.Warning)
                {
                    Log(string.Format(CultureInfo.CurrentCulture, Resources.ProfileEntries, entriesInUse, entries));
                }

                //If there are no columns or rows.
                if (entriesInUse == 0 || it.CaptureObjects.Count == 0)
                {
                    continue;
                }

                //All meters are not supporting parameterized read.
                if ((Client.NegotiatedConformance & (Gurux.DLMS.Enums.Conformance.ParameterizedAccess | Gurux.DLMS.Enums.Conformance.SelectiveAccess)) != 0)
                {
                    try
                    {
                        //Read first row from Profile Generic.
                        object[] rows = (await ReadRowsByEntryAsync(it, 1, 1).ConfigureAwait(false));
                        //If trace is info.
                        if (Trace > TraceLevel.Warning)
                        {
                            StringBuilder sb = new StringBuilder();
                            foreach (object[] row in rows)
                            {
                                foreach (object cell in row)
                                {
                                    if (cell is byte[])
                                    {
                                        sb.Append(GXCommon.ToHex((byte[])cell, true));
                                    }
                                    else
                                    {
                                        sb.Append(Convert.ToString(cell));
                                    }

                                    sb.Append(" | ");
                                }

                                sb.Append("\r\n");
                            }

                            Log(sb.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(string.Format(CultureInfo.CurrentCulture, Resources.FirstProfileRowReadFailed, ex.Message));
                        //Continue reading.
                    }
                }

                //All meters are not supporting parameterized read.
                if ((Client.NegotiatedConformance & (Gurux.DLMS.Enums.Conformance.ParameterizedAccess | Gurux.DLMS.Enums.Conformance.SelectiveAccess)) != 0)
                {
                    try
                    {
                        //Read last day from Profile Generic.
                        object[] rows = (await ReadRowsByRangeAsync(it, DateTime.Now.Date, DateTime.MaxValue).ConfigureAwait(false));
                        //If trace is info.
                        if (Trace > TraceLevel.Warning)
                        {
                            StringBuilder sb = new StringBuilder();
                            foreach (object[] row in rows)
                            {
                                foreach (object cell in row)
                                {
                                    if (cell is byte[])
                                    {
                                        sb.Append(GXCommon.ToHex((byte[])cell, true));
                                    }
                                    else
                                    {
                                        sb.Append(Convert.ToString(cell));
                                    }

                                    sb.Append(" | ");
                                }

                                sb.Append("\r\n");
                            }

                            Log(sb.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(string.Format(CultureInfo.CurrentCulture, Resources.LastDayProfileReadFailed, ex.Message));
                        //Continue reading.
                    }
                }
            }
        }

        /// <summary>
        /// Reads compact-data capture definitions, templates, and buffers, then decodes and traces their rows.
        /// </summary>
        /// <param name="compactData">Compact-data object whose readable capture definition, template, and buffer are retrieved and decoded.</param>
        public void GetCompactData(GXDLMSCompactData compactData) => GetCompactDataAsync(compactData).GetAwaiter().GetResult();
        /// <summary>
        /// Reads compact-data capture definitions, templates, and buffers, then decodes and traces their rows.
        /// </summary>
        /// <param name="compactData">Compact-data object whose readable capture definition, template, and buffer are retrieved and decoded.</param>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task GetCompactDataAsync(GXDLMSCompactData compactData)
        {
            //If trace is info.
            if (Trace > TraceLevel.Warning)
            {
                Log(string.Format(CultureInfo.CurrentCulture, Resources.ReadingObject, compactData.GetType().Name, compactData.Name, compactData.Description));
            }

            //Read Capture objects.
            if (Client.CanRead(compactData, 3))
            {
                await ReadAsync(compactData, 3).ConfigureAwait(false);
            }

            //Read template description.
            if (Client.CanRead(compactData, 5))
            {
                await ReadAsync(compactData, 5).ConfigureAwait(false);
            }

            //Read buffer.
            if (Client.CanRead(compactData, 2))
            {
                await ReadAsync(compactData, 2).ConfigureAwait(false);
            }

            Standard standard = Client.Standard;
            List<DataType> types = new List<DataType>();
            foreach (var c in compactData.CaptureObjects)
            {
                types.Add(c.Key.GetUIDataType(c.Value.AttributeIndex));
            }

            List<object> rows = GXDLMSCompactData.GetData(compactData.TemplateDescription, compactData.Buffer);
            //Convert cols to readable format.
            foreach (GXStructure row in rows)
            {
                for (int col = 0; col != types.Count; ++col)
                {
                    if (types[col] != DataType.None)
                    {
                        row[col] = GXDLMSClient.ChangeType(row[col] as byte[], types[col]);
                    }
                    else if (row[col] is GXArray)
                    {
                        row[col] = GXDLMSTranslator.ValueToXml(row[col]);
                    }
                    else if (row[col] is GXStructure)
                    {
                        row[col] = GXDLMSTranslator.ValueToXml(row[col]);
                    }
                    else if (row[col] is byte[] b)
                    {
                        row[col] = GXDLMSTranslator.ToHex(b);
                    }
                }

                Log(row);
            }
        }

        /// <summary>
        /// Reads and traces readable attributes of supported objects other than profile generics.
        /// </summary>
        /// <remarks>This demonstration reads broadly across the association. Attribute errors are logged and remaining attributes are processed.</remarks>
        public void GetReadOut() => GetReadOutAsync().GetAwaiter().GetResult();
        /// <summary>
        /// Reads and traces readable attributes of supported objects other than profile generics.
        /// </summary>
        /// <remarks>This demonstration reads broadly across the association. Attribute errors are logged and remaining attributes are processed.</remarks>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task GetReadOutAsync()
        {
            foreach (GXDLMSObject it in Client.Objects)
            {
                // Profile generics are read later because they are special cases.
                // (There might be so lots of data and we so not want waste time to read all the data.)
                if (it is GXDLMSProfileGeneric)
                {
                    continue;
                }

                if (it is not IGXDLMSBase dlmsObject)
                {
                    //If interface is not implemented.
                    //Example manufacturer spesific interface.
                    if (Trace > TraceLevel.Error)
                    {
                        Log(string.Format(CultureInfo.CurrentCulture, Resources.UnknownInterface, it.ObjectType));
                    }

                    continue;
                }

                if (Trace > TraceLevel.Warning)
                {
                    Log(string.Format(CultureInfo.CurrentCulture, Resources.ReadingObject, it.GetType().Name, it.Name, it.Description));
                }

                foreach (int pos in dlmsObject.GetAttributeIndexToRead(true))
                {
                    try
                    {
                        if (Client.CanRead(it, pos))
                        {
                            object? val = (await ReadAsync(it, pos).ConfigureAwait(false));
                            if (Trace > TraceLevel.Warning)
                            {
                                ShowValue(val, pos);
                            }
                        }
                        else
                        {
                            Log(string.Format(CultureInfo.CurrentCulture, Resources.AttributeNotReadable, it.GetType().Name, it.Name, pos));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(string.Format(CultureInfo.CurrentCulture, Resources.AttributeReadFailed, it.GetType().Name, it.Name, pos, ex.Message));
                        Log(ex.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Forwards a trace line to each configured subscriber unless tracing is disabled, isolating subscriber exceptions.
        /// </summary>
        /// <param name="line">Formatted communication or diagnostic message.</param>
        void WriteTrace(string line)
        {
            if (Trace != TraceLevel.Off)
            {
                InvokeObserver(OnTrace, line);
            }
        }

        /// <summary>
        /// Sends nonempty attribute operations in an access request and applies the response to the items.
        /// </summary>
        /// <param name="list">Access operations whose results are populated from the response.</param>
        /// <returns>A task representing completion of the operation.</returns>
        async Task ReadByAccessAsync(List<GXDLMSAccessItem> list)
        {
            if (list.Count != 0)
            {
                GXReplyData reply = new GXReplyData();
                byte[][] data = Client.AccessRequest(DateTime.MinValue, list);
                await ReadDataBlockAsync(data, reply).ConfigureAwait(false);
                Client.ParseAccessResponse(list, reply.Data);
            }
        }

    }
}
