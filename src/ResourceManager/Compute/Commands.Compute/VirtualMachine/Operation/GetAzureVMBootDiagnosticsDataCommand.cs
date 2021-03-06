﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using Microsoft.Azure.Commands.Compute.Common;
using Microsoft.Azure.Commands.Compute.Models;
using Microsoft.Azure.Common.Authentication;
using Microsoft.Azure.Common.Authentication.Models;
using Microsoft.Azure.Management.Compute;
using Microsoft.Azure.Management.Storage;
using Microsoft.WindowsAzure.Commands.Sync.Download;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text;

namespace Microsoft.Azure.Commands.Compute
{
    [Cmdlet(VerbsCommon.Get, ProfileNouns.BootDiagnosticsData, DefaultParameterSetName = WindowsParamSet)]
    [OutputType(typeof(PSVirtualMachine), typeof(PSVirtualMachineInstanceView))]
    public class GetAzureVMBootDiagnosticsDataCommand : VirtualMachineBaseCmdlet
    {
        private const string WindowsParamSet = "WindowsParamSet";
        private const string LinuxParamSet = "LinuxParamSet";

        [Parameter(
           Mandatory = true,
           Position = 0,
           ValueFromPipelineByPropertyName = true)]
        [ValidateNotNullOrEmpty]
        public string ResourceGroupName { get; set; }

        [Alias("ResourceName", "VMName")]
        [Parameter(
            Mandatory = true,
            Position = 1,
            ValueFromPipelineByPropertyName = true)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        [Parameter(
            Mandatory = true,
            Position = 2,
            ParameterSetName = WindowsParamSet)]
        public SwitchParameter Windows { get; set; }

        [Parameter(
            Mandatory = true,
            Position = 2,
            ParameterSetName = LinuxParamSet)]
        public SwitchParameter Linux { get; set; }

        [Parameter(
            Mandatory = true,
            Position = 3,
            ParameterSetName = WindowsParamSet,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "Path and name of the output RDP file.")]
        [Parameter(
            Mandatory = false,
            Position = 3,
            ParameterSetName = LinuxParamSet,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "Path and name of the output RDP file.")]
        [ValidateNotNullOrEmpty]
        public string LocalPath { get; set; }

        public override void ExecuteCmdlet()
        {
            base.ExecuteCmdlet();

            ExecuteClientAction(() =>
            {
                var result = this.VirtualMachineClient.GetWithInstanceView(this.ResourceGroupName, this.Name);
                if (result == null || result.VirtualMachine == null)
                {
                    ThrowTerminatingError
                (new ErrorRecord(
                    new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        "no virtual machine")),
                    string.Empty,
                    ErrorCategory.InvalidData,
                    null));
                }

                if (result.VirtualMachine.DiagnosticsProfile == null
                    || result.VirtualMachine.DiagnosticsProfile.BootDiagnostics == null
                    || result.VirtualMachine.DiagnosticsProfile.BootDiagnostics.Enabled == null
                    || !result.VirtualMachine.DiagnosticsProfile.BootDiagnostics.Enabled.Value
                    || result.VirtualMachine.DiagnosticsProfile.BootDiagnostics.StorageUri == null)
                {
                    ThrowTerminatingError
                (new ErrorRecord(
                    new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        "no diagnostic profile enabled")),
                    string.Empty,
                    ErrorCategory.InvalidData,
                    null));
                }

                if (result.VirtualMachine.InstanceView == null
                    || result.VirtualMachine.InstanceView.BootDiagnostics == null)
                {
                    ThrowTerminatingError
                (new ErrorRecord(
                    new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        "no boot diagnostic")),
                    string.Empty,
                    ErrorCategory.InvalidData,
                    null));
                }

                if (this.Windows.IsPresent
                    || (this.Linux.IsPresent && !string.IsNullOrEmpty(this.LocalPath)))
                {
                    var screenshotUri = result.VirtualMachine.InstanceView.BootDiagnostics.ConsoleScreenshotBlobUri;
                    var localFile = this.LocalPath + screenshotUri.Segments[2];
                    DownloadFromBlobUri(screenshotUri, localFile);
                }


                if (this.Linux.IsPresent)
                {
                    var logUri = result.VirtualMachine.InstanceView.BootDiagnostics.SerialConsoleLogBlobUri;

                    var localFile = (this.LocalPath ?? Path.GetTempPath()) + logUri.Segments[2];

                    DownloadFromBlobUri(logUri, localFile);

                    var sb = new StringBuilder();
                    using (var reader = new StreamReader(localFile))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            sb.AppendLine(line);
                        }
                    };

                    WriteObject(sb.ToString());
                }
            });
        }

        private void DownloadFromBlobUri(Uri sourceUri, string localFileInfo)
        {
            BlobUri blobUri;
            string storagekey="";
            if (!BlobUri.TryParseUri(sourceUri, out blobUri))
            {
                throw new ArgumentOutOfRangeException("Source", sourceUri.ToString());
            }

            var storageClient = AzureSession.ClientFactory.CreateClient<StorageManagementClient>(
                        DefaultProfile.Context, AzureEnvironment.Endpoint.ResourceManager);


            var storageService = storageClient.StorageAccounts.GetProperties(this.ResourceGroupName, blobUri.StorageAccountName);
            if (storageService != null)
            {
                var storageKeys = storageClient.StorageAccounts.ListKeys(this.ResourceGroupName, storageService.StorageAccount.Name);
                storagekey = storageKeys.StorageAccountKeys.Key1;
            }

            StorageCredentials storagecred = new StorageCredentials(blobUri.StorageAccountName, storagekey);
            var blob = new CloudBlob(sourceUri, storagecred);

            blob.DownloadToFile(localFileInfo, FileMode.Create);
        }
    }
}
