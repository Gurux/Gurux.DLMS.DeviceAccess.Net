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

using Gurux.DLMS.ASN;
using Gurux.DLMS.Ecdsa;
using Gurux.DLMS.Ecdsa.Enums;
using Gurux.DLMS.Enums;
using Gurux.DLMS.Objects;
using Gurux.DLMS.Objects.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Globalization;
using Resources = Gurux.DLMS.DeviceAccess.Properties.Resources;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace Gurux.DLMS.DeviceAccess
{
    public partial class GXDLMSReader
    {

        /// <summary>
        /// Generates client and meter keys, obtains signed certificates from the specified certificate service,
        /// and imports and verifies the certificates on the meter.
        /// </summary>
        /// <param name="securitySetup">Security setup object used to read system titles and configure the meter's keys and certificates.</param>
        /// <param name="directory">Root directory for generated private keys and signed certificates. The Keys, Keys384, Certificates, and Certificates384 subdirectories are created as needed. Relative paths are resolved against the current directory.</param>
        /// <param name="address">Absolute URL of a certificate-signing service compatible with the Gurux CertificateGenerator API. Certificate signing requests are submitted to this endpoint using an HTTP POST with a JSON body. Defaults to https://certificates.gurux.fi/api/CertificateGenerator.</param>
        /// <exception cref="ArgumentNullException">The security setup object is null.</exception>
        /// <exception cref="ArgumentException">The output directory is null, empty, whitespace, or invalid.</exception>
        /// <exception cref="IOException">An output directory cannot be created or a private key or certificate cannot be saved.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to an output directory or file is denied.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a key-generation, certificate-import, or certificate-export operation.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A certificate exported for verification differs from the certificate imported into the meter.</exception>
        /// <exception cref="HttpRequestException">The signing service cannot be reached or returns an unsuccessful HTTP status.</exception>
        /// <exception cref="OperationCanceledException">The configured certificate-service deadline expires.</exception>
        /// <exception cref="JsonException">The signing service response is not valid JSON.</exception>
        /// <exception cref="InvalidOperationException">The signing service response exceeds the configured size limit, returns an unexpected certificate count or contains a certificate with a different public key.</exception>
        /// <exception cref="Exception">Authentication is insufficient, a signing request does not match the server system title, or the certificate service fails or returns an invalid response.</exception>
        /// <remarks>
        /// <para>Certificate-service requests use the snapshotted <see cref="GXDLMSReaderOptions.CertificateServiceTimeout"/>
        /// and <see cref="GXDLMSReaderOptions.CertificateServiceMaxResponseBytes"/> settings.</para>
        /// <para>Waits synchronously for <see cref="GenerateCertificatesAsync(GXDLMSSecuritySetup, string, string)"/> to finish.</para>
        /// <para>Creates P-256 client keys for digital signatures and key agreement, requests corresponding meter keys,
        /// and saves the signed certificates before importing them. Imported certificates are verified by exporting
        /// them by entity and by serial number. The connection is opened if needed and is left open after the operation.</para>
        /// </remarks>
        public void GenerateCertificates(GXDLMSSecuritySetup securitySetup, string directory, string address = "https://certificates.gurux.fi/api/CertificateGenerator") => GenerateCertificatesAsync(securitySetup, directory, address).GetAwaiter().GetResult();
        /// <summary>
        /// Generates client and meter keys, obtains signed certificates from the specified certificate service,
        /// and imports and verifies the certificates on the meter.
        /// </summary>
        /// <param name="securitySetup">Security setup object used to read system titles and configure the meter's keys and certificates.</param>
        /// <param name="cancellationToken">Cancellation forwarded to the asynchronous implementation; this synchronous method blocks until it completes.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation, or the certificate-service deadline expires.</exception>
        /// <param name="directory">Root directory for generated private keys and signed certificates. The Keys, Keys384, Certificates, and Certificates384 subdirectories are created as needed. Relative paths are resolved against the current directory.</param>
        /// <param name="address">Absolute URL of a certificate-signing service compatible with the Gurux CertificateGenerator API. Certificate signing requests are submitted to this endpoint using an HTTP POST with a JSON body. Defaults to https://certificates.gurux.fi/api/CertificateGenerator.</param>
        /// <exception cref="ArgumentNullException">The security setup object is null.</exception>
        /// <exception cref="ArgumentException">The output directory is null, empty, whitespace, or invalid.</exception>
        /// <exception cref="IOException">An output directory cannot be created or a private key or certificate cannot be saved.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to an output directory or file is denied.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a key-generation, certificate-import, or certificate-export operation.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A certificate exported for verification differs from the certificate imported into the meter.</exception>
        /// <exception cref="HttpRequestException">The signing service cannot be reached or returns an unsuccessful HTTP status.</exception>
        /// <exception cref="JsonException">The signing service response is not valid JSON.</exception>
        /// <exception cref="InvalidOperationException">The signing service response exceeds the configured size limit, returns an unexpected certificate count or contains a certificate with a different public key.</exception>
        /// <exception cref="Exception">Authentication is insufficient, a signing request does not match the server system title, or the certificate service fails or returns an invalid response.</exception>
        /// <remarks>
        /// <para>Certificate-service requests use the snapshotted <see cref="GXDLMSReaderOptions.CertificateServiceTimeout"/>
        /// and <see cref="GXDLMSReaderOptions.CertificateServiceMaxResponseBytes"/> settings.</para>
        /// <para>Waits synchronously for <see cref="GenerateCertificatesAsync(GXDLMSSecuritySetup, string, CancellationToken, string)"/> to finish.</para>
        /// <para>Creates P-256 client keys for digital signatures and key agreement, requests corresponding meter keys,
        /// and saves the signed certificates before importing them. Imported certificates are verified by exporting
        /// them by entity and by serial number. The connection is opened if needed and is left open after the operation.</para>
        /// </remarks>
        public void GenerateCertificates(GXDLMSSecuritySetup securitySetup, string directory, CancellationToken cancellationToken, string address = "https://certificates.gurux.fi/api/CertificateGenerator") => GenerateCertificatesAsync(securitySetup, directory, cancellationToken, address).GetAwaiter().GetResult();
        /// <summary>
        /// Generates client and meter keys, obtains signed certificates from the specified certificate service,
        /// and imports and verifies the certificates on the meter.
        /// </summary>
        /// <param name="securitySetup">Security setup object used to read system titles and configure the meter's keys and certificates.</param>
        /// <param name="directory">Root directory for generated private keys and signed certificates. The Keys, Keys384, Certificates, and Certificates384 subdirectories are created as needed. Relative paths are resolved against the current directory.</param>
        /// <param name="address">Absolute URL of a certificate-signing service compatible with the Gurux CertificateGenerator API. Certificate signing requests are submitted to this endpoint using an HTTP POST with a JSON body. Defaults to https://certificates.gurux.fi/api/CertificateGenerator.</param>
        /// <exception cref="ArgumentNullException">The security setup object is null.</exception>
        /// <exception cref="ArgumentException">The output directory is null, empty, whitespace, or invalid.</exception>
        /// <exception cref="IOException">An output directory cannot be created or a private key or certificate cannot be saved.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to an output directory or file is denied.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a key-generation, certificate-import, or certificate-export operation.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A certificate exported for verification differs from the certificate imported into the meter.</exception>
        /// <exception cref="HttpRequestException">The signing service cannot be reached or returns an unsuccessful HTTP status.</exception>
        /// <exception cref="OperationCanceledException">The configured certificate-service deadline expires.</exception>
        /// <exception cref="JsonException">The signing service response is not valid JSON.</exception>
        /// <exception cref="InvalidOperationException">The signing service response exceeds the configured size limit, returns an unexpected certificate count or contains a certificate with a different public key.</exception>
        /// <exception cref="Exception">Authentication is insufficient, a signing request does not match the server system title, or the certificate service fails or returns an invalid response.</exception>
        /// <returns>A task that completes after key generation, certificate retrieval, local storage, meter import, and export verification have finished.</returns>
        /// <remarks>
        /// <para>Certificate-service requests use the snapshotted <see cref="GXDLMSReaderOptions.CertificateServiceTimeout"/>
        /// and <see cref="GXDLMSReaderOptions.CertificateServiceMaxResponseBytes"/> settings.</para>
        /// <para>Creates P-256 client keys for digital signatures and key agreement and asks the meter to generate its
        /// corresponding keys and signing requests. The certificate service signs the client and meter requests.
        /// Signed certificates are saved locally, imported into the meter, and verified by exporting them by entity
        /// and by serial number. The connection is opened if needed and is left open after the operation.</para>
        /// <para>Meter and certificate-service HTTP I/O are awaited. File serialization and cryptographic dependency calls
        /// are synchronous and cannot be interrupted once started; cancellation is checked between processing stages.</para>
        /// </remarks>
        public Task GenerateCertificatesAsync(GXDLMSSecuritySetup securitySetup, string directory, string address = "https://certificates.gurux.fi/api/CertificateGenerator") => GenerateCertificatesAsync(securitySetup, directory, CancellationToken.None, address);
        /// <summary>
        /// Generates client and meter keys, obtains signed certificates from the specified certificate service,
        /// and imports and verifies the certificates on the meter.
        /// </summary>
        /// <param name="securitySetup">Security setup object used to read system titles and configure the meter's keys and certificates.</param>
        /// <param name="cancellationToken">Cancels session acquisition, initialization, meter and HTTP I/O, and processing between synchronous file or cryptographic calls.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation, or the certificate-service deadline expires.</exception>
        /// <param name="directory">Root directory for generated private keys and signed certificates. The Keys, Keys384, Certificates, and Certificates384 subdirectories are created as needed. Relative paths are resolved against the current directory.</param>
        /// <param name="address">Absolute URL of a certificate-signing service compatible with the Gurux CertificateGenerator API. Certificate signing requests are submitted to this endpoint using an HTTP POST with a JSON body. Defaults to https://certificates.gurux.fi/api/CertificateGenerator.</param>
        /// <exception cref="ArgumentNullException">The security setup object is null.</exception>
        /// <exception cref="ArgumentException">The output directory is null, empty, whitespace, or invalid.</exception>
        /// <exception cref="IOException">An output directory cannot be created or a private key or certificate cannot be saved.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to an output directory or file is denied.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a key-generation, certificate-import, or certificate-export operation.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A certificate exported for verification differs from the certificate imported into the meter.</exception>
        /// <exception cref="HttpRequestException">The signing service cannot be reached or returns an unsuccessful HTTP status.</exception>
        /// <exception cref="JsonException">The signing service response is not valid JSON.</exception>
        /// <exception cref="InvalidOperationException">The signing service response exceeds the configured size limit, returns an unexpected certificate count or contains a certificate with a different public key.</exception>
        /// <exception cref="Exception">Authentication is insufficient, a signing request does not match the server system title, or the certificate service fails or returns an invalid response.</exception>
        /// <returns>A task that completes after key generation, certificate retrieval, local storage, meter import, and export verification have finished.</returns>
        /// <remarks>
        /// <para>Certificate-service requests use the snapshotted <see cref="GXDLMSReaderOptions.CertificateServiceTimeout"/>
        /// and <see cref="GXDLMSReaderOptions.CertificateServiceMaxResponseBytes"/> settings.</para>
        /// <para>Creates P-256 client keys for digital signatures and key agreement and asks the meter to generate its
        /// corresponding keys and signing requests. The certificate service signs the client and meter requests.
        /// Signed certificates are saved locally, imported into the meter, and verified by exporting them by entity
        /// and by serial number. The connection is opened if needed and is left open after the operation.</para>
        /// <para>Meter and certificate-service HTTP I/O are awaited. File serialization and cryptographic dependency calls
        /// are synchronous and cannot be interrupted once started; cancellation is checked between processing stages.</para>
        /// </remarks>
        public async Task GenerateCertificatesAsync(GXDLMSSecuritySetup securitySetup, string directory, CancellationToken cancellationToken, string address = "https://certificates.gurux.fi/api/CertificateGenerator") => await ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(securitySetup);
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            directory = Path.GetFullPath(directory);
            if (!Directory.Exists(Path.Combine(directory, "Keys")))
            {
                Directory.CreateDirectory(Path.Combine(directory, "Keys"));
            }

            if (!Directory.Exists(Path.Combine(directory, "Certificates")))
            {
                Directory.CreateDirectory(Path.Combine(directory, "Certificates"));
            }

            if (!Directory.Exists(Path.Combine(directory, "Keys384")))
            {
                Directory.CreateDirectory(Path.Combine(directory, "Keys384"));
            }

            if (!Directory.Exists(Path.Combine(directory, "Certificates384")))
            {
                Directory.CreateDirectory(Path.Combine(directory, "Certificates384"));
            }

            GXx509Certificate[] certs;
            if (Client.Authentication < Authentication.Low)
            {
                throw new Exception(Resources.HighAuthenticationRequired);
            }

            GXReplyData reply = new GXReplyData();
            await InitializeConnectionAsync().ConfigureAwait(false);
            List<GXCertificateRequest> certifications = new List<GXCertificateRequest>();
            await ReadAsync(securitySetup, 3).ConfigureAwait(false);
            await ReadAsync(securitySetup, 4).ConfigureAwait(false);
            await ReadAsync(securitySetup, 5).ConfigureAwait(false);
            byte[] clientST = securitySetup.ClientSystemTitle;
            if (clientST == null || clientST.Length != 8)
            {
                clientST = Client.Ciphering.SystemTitle;
            }

            //Get client subject.
            string subject = GXAsn1Converter.SystemTitleToSubject(clientST);
            //Generate new digital signature for the client. In this example P-256 keys are used.
            KeyValuePair<GXPublicKey, GXPrivateKey> kp = GXEcdsa.GenerateKeyPair(Ecc.P256);
            //Save private key in PKCS #8 format.
            GXPkcs8 key = new GXPkcs8(kp);
            operationToken.ThrowIfCancellationRequested();
            key.Save(Path.Combine(directory, GXPkcs8.GetFilePath(Ecc.P256, CertificateType.DigitalSignature, clientST)));
            //Generate x509 certificates.
            GXPkcs10 pkc10 = GXPkcs10.CreateCertificateSigningRequest(kp, subject);
            //All certigicates are generated with one request.
            certifications.Add(new GXCertificateRequest(CertificateType.DigitalSignature, pkc10));
            //Generate new key agreement for the client. In this example P-256 keys are used.
            kp = GXEcdsa.GenerateKeyPair(Ecc.P256);
            //Save private key in PKCS #8 format.
            key = new GXPkcs8(kp);
            operationToken.ThrowIfCancellationRequested();
            key.Save(Path.Combine(directory, GXPkcs8.GetFilePath(Ecc.P256, CertificateType.KeyAgreement, clientST)));
            //Generate x509 certificates.
            pkc10 = GXPkcs10.CreateCertificateSigningRequest(kp, subject);
            //All certigicates are generated with one request.
            certifications.Add(new GXCertificateRequest(CertificateType.KeyAgreement, pkc10));
            //Generate public/private key for digital signature.
            if (!(await ReadDataBlockAsync(securitySetup.GenerateKeyPair(Client, CertificateType.DigitalSignature), reply).ConfigureAwait(false)))
            {
                throw new GXDLMSException(reply.Error);
            }

            reply.Clear();
            //Generate public/private key for key agreement.
            if (!(await ReadDataBlockAsync(securitySetup.GenerateKeyPair(Client, CertificateType.KeyAgreement), reply).ConfigureAwait(false)))
            {
                throw new GXDLMSException(reply.Error);
            }

            reply.Clear();
            //Generate certification request.
            if (!(await ReadDataBlockAsync(securitySetup.GenerateCertificate(Client, CertificateType.DigitalSignature), reply).ConfigureAwait(false)))
            {
                throw new GXDLMSException(reply.Error);
            }

            //Generate server certification.
            pkc10 = new GXPkcs10((byte[])reply.Value);
            subject = GXAsn1Converter.SystemTitleToSubject(securitySetup.ServerSystemTitle);
            //Validate subject.
            if (!pkc10.Subject.Contains(subject))
            {
                throw new Exception(string.Format(CultureInfo.CurrentCulture, Resources.ServerSystemTitleMismatch, GXDLMSTranslator.ToHex(securitySetup.ServerSystemTitle), GXAsn1Converter.HexSystemTitleFromSubject(pkc10.Subject)));
            }

            certifications.Add(new GXCertificateRequest(CertificateType.DigitalSignature, pkc10));
            reply.Clear();
            //Generate certification request for key agreement.
            if (!(await ReadDataBlockAsync(securitySetup.GenerateCertificate(Client, CertificateType.KeyAgreement), reply).ConfigureAwait(false)))
            {
                throw new GXDLMSException(reply.Error);
            }

            //Generate server certification.
            pkc10 = new GXPkcs10((byte[])reply.Value);
            //Validate subject.
            if (!pkc10.Subject.Contains(subject))
            {
                throw new Exception(string.Format(CultureInfo.CurrentCulture, Resources.ServerSystemTitleMismatch, GXDLMSTranslator.ToHex(securitySetup.ServerSystemTitle), GXAsn1Converter.HexSystemTitleFromSubject(pkc10.Subject)));
            }

            certifications.Add(new GXCertificateRequest(CertificateType.KeyAgreement, pkc10));
            reply.Clear();
            //Note! There is a limit how many request you can do in a day.
            certs = await GXCertificateService.RequestAsync(address, certifications, operationToken, readerOptions.CertificateServiceTimeout, readerOptions.CertificateServiceMaxResponseBytes).ConfigureAwait(false);
            //Save server certificates.
            foreach (GXx509Certificate it in certs)
            {
                operationToken.ThrowIfCancellationRequested();
                it.Save(Path.Combine(directory, GXx509Certificate.GetFilePath(it)));
            }

            reply.Clear();
            //Import server certificates.
            foreach (GXx509Certificate it in certs)
            {
                if (!(await ReadDataBlockAsync(securitySetup.ImportCertificate(Client, it), reply).ConfigureAwait(false)))
                {
                    throw new GXDLMSException(reply.Error);
                }

                reply.Clear();
            }

            //Export server certificates and verify it.
            foreach (GXx509Certificate it in certs)
            {
                CertificateEntity entity;
                byte[] st;
                if (it.Subject.Contains(GXAsn1Converter.SystemTitleToSubject(securitySetup.ServerSystemTitle)))
                {
                    st = securitySetup.ServerSystemTitle;
                    entity = CertificateEntity.Server;
                }
                else if (it.Subject.Contains(GXAsn1Converter.SystemTitleToSubject(clientST)))
                {
                    st = clientST;
                    entity = CertificateEntity.Client;
                }
                else
                {
                    //This is another certificate.
                    continue;
                }

                if (!(await ReadDataBlockAsync(securitySetup.ExportCertificateByEntity(Client, entity, GXDLMSConverter.KeyUsageToCertificateType(it.KeyUsage), st), reply).ConfigureAwait(false)))
                {
                    throw new GXDLMSException(reply.Error);
                }

                //Verify certificate.
                GXx509Certificate exported = new GXx509Certificate((byte[])reply.Value);
                VerifyCertificate(exported, it);

                reply.Clear();
            }

            //Export server certificates using serial number and verify it.
            foreach (GXx509Certificate it in certs)
            {
                if (!(await ReadDataBlockAsync(securitySetup.ExportCertificateBySerial(Client, it.SerialNumber, it.IssuerRaw), reply).ConfigureAwait(false)))
                {
                    throw new GXDLMSException(reply.Error);
                }

                //Verify certificate.
                GXx509Certificate exported = new GXx509Certificate((byte[])reply.Value);
                VerifyCertificate(exported, it);

                reply.Clear();
            }
            operationToken.ThrowIfCancellationRequested();
        }, cancellationToken, false).ConfigureAwait(false);

        /// <summary>
        /// Export client and server certificates from the meter.
        /// </summary>
        /// <param name="securitySetup">Security setup object whose attributes and client and server certificates are read from the meter.</param>
        /// <exception cref="ArgumentNullException">The security setup object is null.</exception>
        /// <param name="directory">Root output directory; certificates are saved in the Gurux Certificates or Certificates384 subdirectory according to their elliptic curve.</param>
        /// <remarks>Creates the Gurux key and certificate subdirectories under the output directory and closes the connection even if export fails. Relative output paths are resolved against the current directory before communication.</remarks>
        /// <exception cref="ArgumentException">The output directory is null, empty, whitespace, or invalid.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to an output directory or file is denied.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a certificate export operation.</exception>
        /// <exception cref="IOException">A certificate cannot be saved.</exception>
        public void ExportMeterCertificates(GXDLMSSecuritySetup securitySetup, string directory) => ExportMeterCertificatesAsync(securitySetup, directory).GetAwaiter().GetResult();
        /// <summary>
        /// Export client and server certificates from the meter.
        /// </summary>
        /// <param name="securitySetup">Security setup object whose attributes and client and server certificates are read from the meter.</param>
        /// <exception cref="ArgumentNullException">The security setup object is null.</exception>
        /// <param name="cancellationToken">Cancellation forwarded to the asynchronous implementation; this synchronous method blocks until it completes.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <param name="directory">Root output directory; certificates are saved in the Gurux Certificates or Certificates384 subdirectory according to their elliptic curve.</param>
        /// <remarks>Creates the Gurux key and certificate subdirectories under the output directory and closes the connection even if export fails. Relative output paths are resolved against the current directory before communication.</remarks>
        /// <exception cref="ArgumentException">The output directory is null, empty, whitespace, or invalid.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to an output directory or file is denied.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a certificate export operation.</exception>
        /// <exception cref="IOException">A certificate cannot be saved.</exception>
        public void ExportMeterCertificates(GXDLMSSecuritySetup securitySetup, string directory, CancellationToken cancellationToken) => ExportMeterCertificatesAsync(securitySetup, directory, cancellationToken).GetAwaiter().GetResult();
        /// <summary>
        /// Export client and server certificates from the meter.
        /// </summary>
        /// <param name="securitySetup">Security setup object whose attributes and client and server certificates are read from the meter.</param>
        /// <exception cref="ArgumentNullException">The security setup object is null.</exception>
        /// <param name="directory">Root output directory; certificates are saved in the Gurux Certificates or Certificates384 subdirectory according to their elliptic curve.</param>
        /// <remarks>Creates the Gurux key and certificate subdirectories under the output directory and closes the connection even if export fails. Relative output paths are resolved against the current directory before communication.</remarks>
        /// <exception cref="ArgumentException">The output directory is null, empty, whitespace, or invalid.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to an output directory or file is denied.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a certificate export operation.</exception>
        /// <exception cref="IOException">A certificate cannot be saved.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        public Task ExportMeterCertificatesAsync(GXDLMSSecuritySetup securitySetup, string directory) => ExportMeterCertificatesAsync(securitySetup, directory, CancellationToken.None);
        /// <summary>
        /// Export client and server certificates from the meter.
        /// </summary>
        /// <param name="securitySetup">Security setup object whose attributes and client and server certificates are read from the meter.</param>
        /// <exception cref="ArgumentNullException">The security setup object is null.</exception>
        /// <param name="cancellationToken">Cancels session acquisition, initialization, meter and HTTP I/O, and processing between synchronous file or cryptographic calls.</param>
        /// <exception cref="OperationCanceledException">Cancellation is requested before or during the operation.</exception>
        /// <param name="directory">Root output directory; certificates are saved in the Gurux Certificates or Certificates384 subdirectory according to their elliptic curve.</param>
        /// <remarks>Creates the Gurux key and certificate subdirectories under the output directory and closes the connection even if export fails. Relative output paths are resolved against the current directory before communication.</remarks>
        /// <exception cref="ArgumentException">The output directory is null, empty, whitespace, or invalid.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to an output directory or file is denied.</exception>
        /// <exception cref="GXDLMSException">The meter rejects a certificate export operation.</exception>
        /// <exception cref="IOException">A certificate cannot be saved.</exception>
        /// <returns>A task representing completion of the operation.</returns>
        public async Task ExportMeterCertificatesAsync(GXDLMSSecuritySetup securitySetup, string directory, CancellationToken cancellationToken) => await ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(securitySetup);
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            directory = Path.GetFullPath(directory);
            try
            {
                await InitializeConnectionAsync().ConfigureAwait(false);
                await ReadAsync(securitySetup, 3).ConfigureAwait(false);
                //Client private keys are saved to this directory.
                //Client might be different system title for each meter.
                if (!Directory.Exists(Path.Combine(directory, "Keys")))
                {
                    Directory.CreateDirectory(Path.Combine(directory, "Keys"));
                }

                if (!Directory.Exists(Path.Combine(directory, "Certificates")))
                {
                    Directory.CreateDirectory(Path.Combine(directory, "Certificates"));
                }

                if (!Directory.Exists(Path.Combine(directory, "Keys384")))
                {
                    Directory.CreateDirectory(Path.Combine(directory, "Keys384"));
                }

                if (!Directory.Exists(Path.Combine(directory, "Certificates384")))
                {
                    Directory.CreateDirectory(Path.Combine(directory, "Certificates384"));
                }

                await ReadAsync(securitySetup, 5).ConfigureAwait(false);
                await ReadAsync(securitySetup, 6).ConfigureAwait(false);
                //Export meter certificates and save them.
                GXReplyData reply = new GXReplyData();
                foreach (GXDLMSCertificateInfo it in securitySetup.Certificates)
                {
                    reply.Clear();
                    //Export certification and verify it.
                    if (!(await ReadDataBlockAsync(securitySetup.ExportCertificateBySerial(Client, it.SerialNumber, it.IssuerRaw), reply).ConfigureAwait(false)))
                    {
                        throw new GXDLMSException(reply.Error);
                    }

                    GXx509Certificate cert = new GXx509Certificate((byte[])reply.Value);
                    string path = Path.Combine(directory, GXx509Certificate.GetFilePath(cert));
                    operationToken.ThrowIfCancellationRequested();
                    cert.Save(path);
                }
            }
            finally
            {
                if (operationToken.IsCancellationRequested)
                {
                    connected = false;
                    await media.CloseAsync().ConfigureAwait(false);
                }
                else
                {
                    await CloseAsync().ConfigureAwait(false);
                }
            }
            operationToken.ThrowIfCancellationRequested();
        }, cancellationToken, false).ConfigureAwait(false);

        /// <summary>Checks that an exported certificate equals the certificate previously imported.</summary>
        /// <param name="exported">Certificate returned by the meter.</param>
        /// <param name="expected">Certificate previously imported into the meter.</param>
        /// <exception cref="ArgumentOutOfRangeException">The exported certificate differs from the expected certificate.</exception>
        private static void VerifyCertificate(GXx509Certificate exported, GXx509Certificate expected)
        {
            if (!exported.Equals(expected))
            {
                throw new ArgumentOutOfRangeException(paramName: null, message: Resources.InvalidServerCertificate);
            }
        }

    }
}
