/* SPDX-License-Identifier: GPL-2.0
 *
 * Copyright (C) 2021 Jason A. Donenfeld <Jason@zx2c4.com>. All Rights Reserved.
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Forms;
using System.Threading;
using System.IO.Pipes;

namespace AzireVPN
{
    public partial class MainWindow : Form
    {
        private static readonly string userDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AzireVPN");
        private static readonly string configFile = Path.Combine(userDirectory, "AzireVPN.conf");
        private static readonly string logFile = Path.Combine(userDirectory, "log.bin");
        private static readonly string tokenFile = Path.Combine(userDirectory, "token.txt");
        private static readonly string keypairFile = Path.Combine(userDirectory, "keypair.txt");

        private Tunnel.Ringlogger log;
        private Thread logPrintingThread, transferUpdateThread;
        private volatile bool threadsRunning;
        private UIState state = 0;
        private string token;

        [Flags]
        enum UIState
        {
            LoggedIn = 0x1,
            LoggingIn = 0x2,
            Connecting = 0x4,
            Connected = 0x8,
            FetchedServers = 0x10,
        }

        public MainWindow()
        {
            InitializeComponent();
            Application.ApplicationExit += Application_ApplicationExit;
            reflectState();

            Directory.CreateDirectory(userDirectory); //TODO: lock down permissions with strict DACL!
            try { File.Delete(logFile); } catch { }
            log = new Tunnel.Ringlogger(logFile, "GUI");
            logPrintingThread = new Thread(new ThreadStart(tailLog));
            transferUpdateThread = new Thread(new ThreadStart(tailTransfer));
        }

        private void tailLog()
        {
            var cursor = Tunnel.Ringlogger.CursorAll;
            while (threadsRunning)
            {
                var lines = log.FollowFromCursor(ref cursor);
                foreach (var line in lines)
                    logBox.Invoke(new Action<string>(logBox.AppendText), new object[] { line + "\r\n" });
                try
                {
                    Thread.Sleep(300);
                }
                catch
                {
                    break;
                }
            }
        }

        private void tailTransfer()
        {
            NamedPipeClientStream stream = null;
            try
            {
                while (threadsRunning)
                {
                    while (threadsRunning)
                    {
                        try
                        {
                            stream = Tunnel.Service.GetPipe(configFile);
                            stream.Connect();
                            break;
                        }
                        catch { }
                        Thread.Sleep(1000);
                    }

                    var reader = new StreamReader(stream);
                    stream.Write(Encoding.UTF8.GetBytes("get=1\n\n"));
                    ulong rx = 0, tx = 0;
                    while (threadsRunning)
                    {
                        var line = reader.ReadLine();
                        if (line == null)
                            break;
                        line = line.Trim();
                        if (line.Length == 0)
                            break;
                        if (line.StartsWith("rx_bytes="))
                            rx += ulong.Parse(line.Substring(9));
                        else if (line.StartsWith("tx_bytes="))
                            tx += ulong.Parse(line.Substring(9));
                    }
                    Invoke(new Action<ulong, ulong>(updateTransferTitle), new object[] { rx, tx });
                    stream.Close();
                    Thread.Sleep(1000);
                }
            }
            catch { }
            finally
            {
                if (stream != null && stream.IsConnected)
                    stream.Close();
            }
        }

        private void Application_ApplicationExit(object sender, EventArgs e)
        {
            Tunnel.Service.Remove(configFile, true);
        }

        private void reflectState()
        {
            loginButton.Enabled = (state & UIState.LoggingIn) == 0 && (state & UIState.Connecting) == 0 && (state & UIState.Connected) == 0;
            usernameBox.Enabled = passwordBox.Enabled = (state & UIState.LoggedIn) == 0 && (state & UIState.LoggingIn) == 0 && (state & UIState.Connecting) == 0 && (state & UIState.Connected) == 0;
            serverPicker.Enabled = (state & UIState.FetchedServers) != 0 && (state & UIState.Connecting) == 0;
            connectButton.Enabled = (state & UIState.FetchedServers) != 0 && (state & UIState.LoggedIn) != 0 && (state & UIState.Connecting) == 0;
            connectButton.Text = (state & UIState.Connected) == 0 ? "&Connect" : "&Disconnect";
            loginButton.Text = (state & UIState.LoggedIn) == 0 ? "&Login" : "&Logout";
        }

        private async void loadToken()
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(tokenFile);
                if (lines.Length < 2)
                    return;
                usernameBox.Text = lines[0];
                token = lines[1];
                state |= UIState.LoggedIn;
                reflectState();
            }
            catch { }
        }

        private async void loadServers()
        {
            log.Write("Loading server list");
            serverPicker.Items.Add("Loading...");
            serverPicker.SelectedIndex = 0;
            List<VpnServer> servers;
            try
            {
                servers = await VpnApi.GetServers();
            }
            catch (Exception ex)
            {
                log.Write("Server fetch error: " + ex.Message);
                return;
            }
            if (servers.Count == 0)
            {
                log.Write("No servers found");
                return;
            }
            serverPicker.Items.Clear();
            foreach (var server in servers)
                serverPicker.Items.Add(server);
            serverPicker.SelectedIndex = 0;
            state |= UIState.FetchedServers;
            reflectState();
        }

        private void MainWindow_Load(object sender, EventArgs e)
        {
            threadsRunning = true;
            logPrintingThread.Start();
            transferUpdateThread.Start();
            loadToken();
            loadServers();
        }

        private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            threadsRunning = false;
            logPrintingThread.Interrupt();
            transferUpdateThread.Interrupt();
            try { logPrintingThread.Join(); } catch { }
            try { transferUpdateThread.Join(); } catch { }
        }

        private static string formatBytes(ulong bytes)
        {
            decimal d = bytes;
            string selectedUnit = null;
            foreach (string unit in new string[] { "B", "KiB", "MiB", "GiB", "TiB" })
            {
                selectedUnit = unit;
                if (d < 1024)
                    break;
                d /= 1024;
            }
            return string.Format("{0:0.##} {1}", d, selectedUnit);
        }

        private void updateTransferTitle(ulong rx, ulong tx)
        {
            var titleBase = Text;
            var idx = titleBase.IndexOf(" - ");
            if (idx != -1)
                titleBase = titleBase.Substring(0, idx);
            if (rx == 0 && tx == 0)
                Text = titleBase;
            else
                Text = string.Format("{0} - rx: {1}, tx: {2}", titleBase, formatBytes(rx), formatBytes(tx));
        }

        private async void loginButton_Click(object sender, EventArgs e)
        {
            if ((state & UIState.LoggedIn) != 0)
            {
                state |= UIState.LoggingIn;
                state &= ~UIState.LoggedIn;
                usernameBox.Text = string.Empty;
                reflectState();
                try
                {
                    log.Write("Logging out");
                    await Task.Run(() => File.Delete(tokenFile));
                    VpnApi.Logout(token);
                }
                catch (Exception ex)
                {
                    log.Write("Failed to log out: " + ex.Message);
                }
                token = null;
                state &= ~UIState.LoggingIn;
                reflectState();
                return;
            }
            state |= UIState.LoggingIn;
            reflectState();
            log.Write("Logging in to generate token");
            try
            {
                var newToken = await VpnApi.Login(usernameBox.Text, passwordBox.Text);
                passwordBox.Text = string.Empty;
                await File.WriteAllLinesAsync(tokenFile, new string[] { usernameBox.Text, newToken });
                state &= ~UIState.LoggingIn;
                state |= UIState.LoggedIn;
                token = newToken;
            }
            catch (Exception ex)
            {
                log.Write("Failed to log in: " + ex.Message);
                state &= ~UIState.LoggingIn; ;
            }
            reflectState();
        }

        private async void serverPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if ((state & UIState.Connected) != 0)
            {
                state |= UIState.Connecting;
                state &= ~UIState.Connected;
                reflectState();
                await Task.Run(() => Tunnel.Service.Remove(configFile, true));
                updateTransferTitle(0, 0);
                connectButton_Click(sender, e);
            }
        }

        private async void connectButton_Click(object sender, EventArgs e)
        {
            if ((state & UIState.Connected) != 0)
            {
                state |= UIState.Connecting;
                state &= ~UIState.Connected;
                reflectState();
                await Task.Run(() => Tunnel.Service.Remove(configFile, true));
                updateTransferTitle(0, 0);
                state &= ~UIState.Connecting;
                reflectState();
                return;
            }

            state |= UIState.Connecting;
            reflectState();
            try
            {
                Tunnel.Keypair keypair;

                try
                {
                    var lines = await File.ReadAllLinesAsync(keypairFile);
                    if (lines.Length < 2)
                        return;
                    keypair = new Tunnel.Keypair(lines[0], lines[1]);
                    log.Write("Loaded cached keypair from file");
                }
                catch
                {
                    keypair = Tunnel.Keypair.Generate();
                    await File.WriteAllLinesAsync(keypairFile, new string[] { keypair.Public, keypair.Private });
                    log.Write("Generated new keypair");
                }
                var config = await VpnApi.Connect((VpnServer)serverPicker.SelectedItem, token, keypair);
                await File.WriteAllBytesAsync(configFile, Encoding.UTF8.GetBytes(config));
                await Task.Run(() => Tunnel.Service.Add(configFile));
                state |= UIState.Connected;
            }
            catch (Exception ex)
            {
                log.Write("Failed to connect: " + ex.Message);
            }
            state &= ~UIState.Connecting;
            reflectState();
        }
    }
}
