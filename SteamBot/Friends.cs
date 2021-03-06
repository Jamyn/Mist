﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Timers;
using BrightIdeasSoftware;
using System.Windows.Forms;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using SteamKit2;
using System.Collections;

namespace MistClient
{
    public partial class Friends : Form
    {
        public static bool chat_opened = false;
        public static Chat chat;
        SteamBot.Bot bot;
        static int TimerInterval = 30000;
        System.Timers.Timer refreshTimer = new System.Timers.Timer(TimerInterval);
        private NotifyIcon trayIcon;
        private ContextMenu trayMenu;
        public byte[] AvatarHash { get; set; } // checking if update is necessary
        SteamFriends SteamFriends;
        string mist_ver = "2.0.0";
        int form_friendsHeight;
        int form_friendreqHeight;
        bool minimizeToTray = true;

        public Friends(SteamBot.Bot bot, string username)
        {
            InitializeComponent();
            this.Text = "Friends - Mist v" + mist_ver;
            this.steam_name.Text = username;
            this.bot = bot;
            this.steam_name.ContextMenuStrip = menu_status;
            this.steam_status.ContextMenuStrip = menu_status;
            this.label1.ContextMenuStrip = menu_status;
            this.minimizeToTrayOnCloseToolStripMenuItem.Checked = true;
            ListFriends.friends = this;
            form_friendsHeight = friends_list.Height;
            form_friendreqHeight = list_friendreq.Height;

            refreshTimer.Interval = TimerInterval;
            refreshTimer.Elapsed += (sender, e) => OnTimerElapsed(sender, e);
            refreshTimer.Start();            

            // Create a simple tray menu with only one item.
            trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("Show", OnTrayIconDoubleClick);
            trayMenu.MenuItems.Add("Exit", OnExit);

            // Create a tray icon. In this example we use a
            // standard system icon for simplicity, but you
            // can of course use your own custom icon too.
            trayIcon = new NotifyIcon();
            trayIcon.Text = "Mist";
            Bitmap bmp = Properties.Resources.mist_icon;
            trayIcon.Icon = Icon.FromHandle(bmp.GetHicon());

            // Add menu to tray icon and show it.
            trayIcon.ContextMenu = trayMenu;
            trayIcon.Visible = false;

            trayIcon.DoubleClick += new System.EventHandler(this.OnTrayIconDoubleClick);
        }

        public bool IsInGame()
        {
            try
            {
                string gameName = bot.SteamFriends.GetFriendGamePlayedName(bot.SteamUser.SteamID);
                return !string.IsNullOrEmpty(gameName);
            }
            catch
            {
                return false;
            }
        }

        public bool IsOnline()
        {
            try
            {
                return bot.SteamFriends.GetPersonaState() != SteamKit2.EPersonaState.Offline;
            }
            catch { return false; }
        }

        Bitmap GetHolder()
        {
            if (IsInGame())
                return MistClient.Properties.Resources.IconIngame;

            if (IsOnline())
                return MistClient.Properties.Resources.IconOnline;

            return MistClient.Properties.Resources.IconOffline;
        }
        Bitmap GetAvatar(string path)
        {
            try
            {
                if (path == null)
                    return MistClient.Properties.Resources.IconUnknown;
                return (Bitmap)Bitmap.FromFile(path);
            }
            catch
            {
                return MistClient.Properties.Resources.IconUnknown;
            }
        }

        Bitmap ComposeAvatar(string path)
        {
            Bitmap holder = GetHolder();
            Bitmap avatar = GetAvatar(path);

            Graphics gfx = null;
            try
            {
                gfx = Graphics.FromImage(holder);
                gfx.DrawImage(avatar, new Rectangle(4, 4, avatar.Width, avatar.Height));
            }
            finally
            {
                gfx.Dispose();
            }

            return holder;
        }

        void AvatarDownloaded(AvatarDownloadDetails details)
        {
            try
            {
                if (avatarBox.InvokeRequired)
                {
                    avatarBox.Invoke(new MethodInvoker(() =>
                    {
                        AvatarDownloaded(details);
                    }
                    ));
                }
                else
                {
                    avatarBox.Image = ComposeAvatar((details.Success ? details.Filename : null));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FriendControl", "Unable to compose avatar: {0}", ex.Message);
            }
        }

        public static bool IsZeros(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 0)
                    return false;
            }
            return true;
        }

        void OnTrayIconDoubleClick(object sender, EventArgs e)
        {
            Visible = true;
            ShowInTaskbar = true;
            trayIcon.Visible = false;
            this.Show();
            this.Activate();
        }

        void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            byte[] avatarHash = bot.SteamFriends.GetFriendAvatar(bot.SteamUser.SteamID);
            bool validHash = avatarHash != null && !IsZeros(avatarHash);

            if ((AvatarHash == null && !validHash && avatarBox.Image != null) || (AvatarHash != null && AvatarHash.SequenceEqual(avatarHash)))
            {
                // avatar is already up to date, no operations necessary
            }
            else if (validHash)
            {
                AvatarHash = avatarHash;
                CDNCache.DownloadAvatar(bot.SteamUser.SteamID, avatarHash, AvatarDownloaded);
            }
            else
            {
                AvatarHash = null;
                avatarBox.Image = ComposeAvatar(null);
            }
            bot.LoadFriends();
            friends_list.SetObjects(ListFriends.Get());
            Console.WriteLine("Friends list refreshed.");
        }

        private void friends_list_ItemActivate(object sender, EventArgs e)
        {
            bot.main.Invoke((Action)(() =>
            {
                string selected = "";
                try
                {
                    selected = friends_list.SelectedItem.Text;
                }
                catch
                {
                    selected = null;
                }
                if (selected != null)
                {
                    ulong sid = ListFriends.GetSID(selected);
                    if (!chat_opened)
                    {
                        chat = new Chat(bot);
                        chat.AddChat(selected, sid);
                        chat.Show();
                        chat.Activate();
                        chat_opened = true;
                    }
                    else
                    {
                        bool found = false;
                        foreach (TabPage tab in chat.ChatTabControl.TabPages)
                        {
                            if (tab.Text == selected)
                            {
                                chat.ChatTabControl.SelectedTab = tab;
                                chat.Activate();
                                found = true;
                            }
                        }
                        if (!found)
                        {
                            chat.AddChat(selected, sid);
                            chat.Activate();
                        }
                    }
                }
            }));
        }

        public void SetObject(System.Collections.IEnumerable collection)
        {
            friends_list.SetObjects(collection);
        }

        private void Friends_FormClosed(object sender, FormClosedEventArgs e)
        {
            trayIcon.Icon = null;
            trayIcon.Visible = false;
            trayIcon.Dispose();
        }

        private void label1_MouseHover(object sender, EventArgs e)
        {
            label1.ForeColor = SystemColors.ControlText;
        }

        private void label1_MouseLeave(object sender, EventArgs e)
        {
            label1.ForeColor = SystemColors.ControlDarkDark;
        }

        private void label1_Click(object sender, EventArgs e)
        {
            menu_status.Show(label1, Cursor.HotSpot.X + 4, Cursor.HotSpot.Y + 4);
        }

        private void onlineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bot.SteamFriends.SetPersonaState(SteamKit2.EPersonaState.Online);
            this.steam_status.Text = bot.SteamFriends.GetPersonaState().ToString();
        }

        private void awayToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bot.SteamFriends.SetPersonaState(SteamKit2.EPersonaState.Away);
            this.steam_status.Text = bot.SteamFriends.GetPersonaState().ToString();
        }

        private void busyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bot.SteamFriends.SetPersonaState(SteamKit2.EPersonaState.Busy);
            this.steam_status.Text = bot.SteamFriends.GetPersonaState().ToString();
        }

        private void lookingToPlayToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bot.SteamFriends.SetPersonaState(SteamKit2.EPersonaState.LookingToPlay);
            this.steam_status.Text = bot.SteamFriends.GetPersonaState().ToString();
        }

        private void lookingToTradeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bot.SteamFriends.SetPersonaState(SteamKit2.EPersonaState.LookingToTrade);
            this.steam_status.Text = bot.SteamFriends.GetPersonaState().ToString();
        }

        private void snoozeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bot.SteamFriends.SetPersonaState(SteamKit2.EPersonaState.Snooze);
            this.steam_status.Text = bot.SteamFriends.GetPersonaState().ToString();
        }

        private void offlineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bot.SteamFriends.SetPersonaState(SteamKit2.EPersonaState.Offline);
            this.steam_status.Text = bot.SteamFriends.GetPersonaState().ToString();
        }

        private void changeProfileNameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProfileName changeProfile = new ProfileName(bot);
            changeProfile.ShowDialog();
        }

        private void label_addfriend_MouseHover(object sender, EventArgs e)
        {
            label_addfriend.ForeColor = SystemColors.ControlText;
            label_addfriend2.ForeColor = SystemColors.ControlText;
        }

        private void label_addfriend_MouseLeave(object sender, EventArgs e)
        {
            label_addfriend.ForeColor = SystemColors.ControlDarkDark;
            label_addfriend2.ForeColor = SystemColors.ControlDarkDark;
        }

        private void label_addfriend2_MouseHover(object sender, EventArgs e)
        {
            label_addfriend.ForeColor = SystemColors.ControlText;
            label_addfriend2.ForeColor = SystemColors.ControlText;
        }

        private void label_addfriend2_MouseLeave(object sender, EventArgs e)
        {
            label_addfriend.ForeColor = SystemColors.ControlDarkDark;
            label_addfriend2.ForeColor = SystemColors.ControlDarkDark;
        }

        private void label_addfriend_Click(object sender, EventArgs e)
        {
            AddFriend addFriend = new AddFriend(bot);
            addFriend.ShowDialog();
        }

        private void label_addfriend2_Click(object sender, EventArgs e)
        {
            AddFriend addFriend = new AddFriend(bot);
            addFriend.ShowDialog();
        }

        private void friends_list_BeforeSearching(object sender, BeforeSearchingEventArgs e)
        {
            e.Canceled = true;
        }

        private void showBackpackToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            // Your backpack
            ulong sid = bot.SteamUser.SteamID;
            ShowBackpack showBP = new ShowBackpack(bot, sid);
            showBP.Show();
            showBP.Activate();
        }

        private void showBackpackToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Friend's backpack
            ulong sid = Convert.ToUInt64(column_sid.GetValue(friends_list.SelectedItem.RowObject));
            ShowBackpack showBP = new ShowBackpack(bot, sid);
            showBP.Show();
            showBP.Activate();            
        }

        private void openChatToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bot.main.Invoke((Action)(() =>
            {
                if (friends_list.SelectedItem != null)
                {
                    ulong sid = Convert.ToUInt64(column_sid.GetValue(friends_list.SelectedItem.RowObject));
                    string selected = bot.SteamFriends.GetFriendPersonaName(sid);
                    if (!chat_opened)
                    {
                        chat = new Chat(bot);
                        chat.AddChat(selected, sid);
                        chat.Show();
                        chat.Focus();
                        chat_opened = true;
                    }
                    else
                    {
                        bool found = false;
                        foreach (TabPage tab in chat.ChatTabControl.TabPages)
                        {
                            if (tab.Text == selected)
                            {
                                chat.ChatTabControl.SelectedTab = tab;
                                chat.Focus();
                                found = true;
                            }
                        }
                        if (!found)
                        {
                            chat.AddChat(selected, sid);
                            chat.Focus();
                        }
                    }
                }
            }));
        }

        private void inviteToTradeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bot.main.Invoke((Action)(() =>
            {
                ulong sid = Convert.ToUInt64(column_sid.GetValue(friends_list.SelectedItem.RowObject));
                string selected = bot.SteamFriends.GetFriendPersonaName(sid);
                if (!chat_opened)
                {
                    chat = new Chat(bot);
                    chat.AddChat(selected, sid);
                    chat.Show();
                    chat.Focus();
                    chat_opened = true;
                    chat.chatTab.tradeClicked();
                }
                else
                {
                    bool found = false;
                    foreach (TabPage tab in Friends.chat.ChatTabControl.TabPages)
                    {
                        if (tab.Text == selected)
                        {
                            found = true;
                            tab.Invoke((Action)(() =>
                            {
                                foreach (var item in tab.Controls)
                                {
                                    chat.chatTab = (ChatTab) item;
                                    chat.chatTab.tradeClicked();
                                }
                            }));
                            return;
                        }
                    }
                    if (!found)
                    {
                        chat.AddChat(selected, sid);
                        chat.Focus();
                        chat.chatTab.tradeClicked();
                    }
                }                
            }));
        }

        private void removeFriendToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (friends_list.SelectedItem != null)
            {
                ulong sid = Convert.ToUInt64(column_sid.GetValue(friends_list.SelectedItem.RowObject));
                bot.SteamFriends.RemoveFriend(sid);
                bot.friends.Remove(sid);
                ListFriends.Remove(sid);
                friends_list.RemoveObject(friends_list.SelectedItem.RowObject);
                MessageBox.Show("You have removed " + bot.SteamFriends.GetFriendPersonaName(sid),
                        "Remove Friend",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Exclamation,
                        MessageBoxDefaultButton.Button1);
            }
        }

        private void blockFriendToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ulong sid = Convert.ToUInt64(column_sid.GetValue(friends_list.SelectedItem.RowObject));
            string selected = bot.SteamFriends.GetFriendPersonaName(sid);
            bot.SteamFriends.IgnoreFriend(sid);
            MessageBox.Show("You have blocked " + selected,
                    "Block Friend",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Exclamation,
                    MessageBoxDefaultButton.Button1);
        }

        public static string HTTPRequest(string url)
        {
            var result = "";
            try
            {
                using (var webClient = new WebClient())
                {
                    using (var stream = webClient.OpenRead(url))
                    {
                        using (var streamReader = new StreamReader(stream))
                        {
                            result = streamReader.ReadToEnd();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var wtf = ex.Message;
            }

            return result;
        }

        public static string ParseBetween(string Subject, string Start, string End)
        {
            return Regex.Match(Subject, Regex.Replace(Start, @"[][{}()*+?.\\^$|]", @"\$0") + @"\s*(((?!" + Regex.Replace(Start, @"[][{}()*+?.\\^$|]", @"\$0") + @"|" + Regex.Replace(End, @"[][{}()*+?.\\^$|]", @"\$0") + @").)+)\s*" + Regex.Replace(End, @"[][{}()*+?.\\^$|]", @"\$0"), RegexOptions.IgnoreCase).Value.Replace(Start, "").Replace(End, "");
        }

        private void steamRepToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // This is a beta feature of SteamRep. Mattie! has informed me that this feature shouldn't be depended on and may die in the future.
            // Don't depend on this.
            try
            {
                if (friends_list.SelectedItem.Text != null)
                {
                    ulong sid = Convert.ToUInt64(column_sid.GetValue(friends_list.SelectedItem.RowObject));
                    string url = "http://steamrep.com/api/beta/reputation/" + sid;
                    string response = HTTPRequest(url);
                    if (response != "")
                    {
                        string status = ParseBetween(response, "<reputation>", "</reputation>");
                        if (status == "")
                        {
                            MessageBox.Show("User has no special reputation.",
                            "SteamRep Status",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Exclamation,
                            MessageBoxDefaultButton.Button1);
                        }
                        else
                        {
                            MessageBox.Show(status,
                            "SteamRep Status",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Exclamation,
                            MessageBoxDefaultButton.Button1);
                        }
                    }
                }
            }
            catch
            {

            }
        }        

        private void OnExit(object sender, EventArgs e)
        {
            Application.Exit();
            Environment.Exit(0);
        }

        private void Friends_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (minimizeToTray)
            {
                Visible = false;
                ShowInTaskbar = false;
                trayIcon.Visible = true;
                trayIcon.ShowBalloonTip(5000, "Mist has been minimized to tray", "To restore Mist, double-click the tray icon.", ToolTipIcon.Info);
                e.Cancel = true;
            }
            else
            {
                trayIcon.Visible = false;
                trayIcon.Icon = null;
                trayIcon.Dispose();
                OnExit(sender, e);
            }
        }

        private void aboutMistToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Mist is written by waylaidwanderer (http://steamcommunity.com/id/waylaidwanderer)."
                + "\nA large part of it was built using the underlying functions of SteamBot (https://github.com/Jessecar96/SteamBot/), and I thank all of SteamBot's contributors"
                + " for making Mist possible.",
                        "About",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1);
        }

        private void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string url = "http://www.thectscommunity.com/dev/mist_ver.php";
            string response = HTTPRequest(url);
            if (response != "")
            {
                if (response != mist_ver)
                {
                    string message = "There is a new version of Mist available. Would you like to be taken to http://steamcommunity.com/groups/MistClient/discussions/0/810919057023360607/ for the latest release? Click No to simply close this message box.";
                    DialogResult choice = MessageBox.Show(new Form() { TopMost = true }, message,
                                    "Outdated",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning,
                                    MessageBoxDefaultButton.Button1);
                    switch (choice)
                    {
                        case DialogResult.Yes:
                            System.Diagnostics.Process.Start("http://steamcommunity.com/groups/MistClient/discussions/0/810919057023360607/");
                            break;
                        case DialogResult.No:
                            break;
                    }
                }
                else
                {
                    MessageBox.Show("Congratulations, Mist is up-to-date! Thank you for using Mist :)",
                        "Latest Version",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1);
                }
            }
        }

        public void GrowFriends()
        {
            bot.main.Invoke((Action)(() =>
            {
                if (!list_friendreq.Visible)
                {
                    friends_list.Height = friends_list.Height + list_friendreq.Height;
                    friends_list.Location = new Point(friends_list.Left, friends_list.Top - list_friendreq.Height);
                }
            }));
        }

        public void ShrinkFriends()
        {
            bot.main.Invoke((Action)(() =>
            {
                if (list_friendreq.Visible)
                {
                    friends_list.Height = friends_list.Height - list_friendreq.Height;
                    friends_list.Location = new Point(friends_list.Left, friends_list.Top + list_friendreq.Height);
                }
            }));
        }

        private void Friends_Load(object sender, EventArgs e)
        {
            friends_list.Height = friends_list.Height + list_friendreq.Height;
            friends_list.Location = new Point(friends_list.Left, friends_list.Top - list_friendreq.Height);
            byte[] avatarHash = bot.SteamFriends.GetFriendAvatar(bot.SteamUser.SteamID);
            bool validHash = avatarHash != null && !IsZeros(avatarHash);

            if ((AvatarHash == null && !validHash && avatarBox.Image != null) || (AvatarHash != null && AvatarHash.SequenceEqual(avatarHash)))
            {
                // avatar is already up to date, no operations necessary
            }
            else if (validHash)
            {
                AvatarHash = avatarHash;
                CDNCache.DownloadAvatar(bot.SteamUser.SteamID, avatarHash, AvatarDownloaded);
            }
            else
            {
                AvatarHash = null;
                avatarBox.Image = ComposeAvatar(null);
            }
        }

        public void NotifyFriendRequest()
        {
            bot.main.Invoke((Action)(() =>
            {
                if (!list_friendreq.Visible)
                {
                    list_friendreq.Visible = true;
                    ShrinkFriends();
                }
            }));
        }

        public void HideFriendRequests()
        {
            bot.main.Invoke((Action)(() =>
            {
                if (list_friendreq.Visible)
                {
                    list_friendreq.Visible = false;
                    friends_list.Height = friends_list.Height + list_friendreq.Height;
                    friends_list.Location = new Point(friends_list.Left, friends_list.Top - list_friendreq.Height);
                    list_friendreq.SetObjects(ListFriendRequests.Get());
                }
            }));
        }

        private void viewGameInfoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (friends_list.SelectedItem != null)
            {
                ulong SteamID = Convert.ToUInt64(column_sid.GetValue(friends_list.SelectedItem.RowObject));
            }
        }

        private void viewProfileToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (friends_list.SelectedItem != null)
            {
                string base_url = "http://steamcommunity.com/profiles/";
                ulong SteamID = Convert.ToUInt64(column_sid.GetValue(friends_list.SelectedItem.RowObject));
                base_url += SteamID.ToString();
                System.Diagnostics.Process.Start(base_url);
            }
        }

        private void acceptFriendRequestToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (list_friendreq.SelectedItem != null)
            {
                ulong SteamID = Convert.ToUInt64(column_friendreq_sid.GetValue(list_friendreq.SelectedItem.RowObject));
                bot.SteamFriends.AddFriend(SteamID);
                friends_list.AddObject(list_friendreq.SelectedItem.RowObject);
                list_friendreq.SelectedItem.Remove();
                ListFriendRequests.Remove(SteamID);
            }
        }

        private void denyFriendRequestToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (list_friendreq.SelectedItem != null)
            {
                ulong SteamID = Convert.ToUInt64(column_friendreq_sid.GetValue(list_friendreq.SelectedItem.RowObject));
                bot.SteamFriends.IgnoreFriend(SteamID);
                list_friendreq.SelectedItem.Remove();
                ListFriendRequests.Remove(SteamID);
            }
        }

        private void viewProfileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (list_friendreq.SelectedItem != null)
            {
                ulong SteamID = Convert.ToUInt64(column_friendreq_sid.GetValue(list_friendreq.SelectedItem.RowObject));
                string base_url = "http://steamcommunity.com/profiles/";
                base_url += SteamID.ToString();
                System.Diagnostics.Process.Start(base_url);
            }
        }

        private void showBackpackToolStripMenuItem2_Click(object sender, EventArgs e)
        {
            if (list_friendreq.SelectedItem != null)
            {
                ulong sid = Convert.ToUInt64(column_friendreq_sid.GetValue(list_friendreq.SelectedItem.RowObject));
                ShowBackpack showBP = new ShowBackpack(bot, sid);
                showBP.Show();
                showBP.Activate();
            }
        }

        private void steamRepStatusToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // This is a beta feature of SteamRep. Mattie! has informed me that this feature shouldn't be depended on and may die in the future.
            // Don't depend on this.
            try
            {
                if (list_friendreq.SelectedItem != null)
                {
                    ulong sid = Convert.ToUInt64(column_friendreq_sid.GetValue(list_friendreq.SelectedItem.RowObject));
                    string url = "http://steamrep.com/api/beta/reputation/" + sid;
                    string response = HTTPRequest(url);
                    if (response != "")
                    {
                        string status = ParseBetween(response, "<reputation>", "</reputation>");
                        if (status == "")
                        {
                            MessageBox.Show("User has no special reputation.",
                            "SteamRep Status",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Exclamation,
                            MessageBoxDefaultButton.Button1);
                        }
                        else
                        {
                            MessageBox.Show(status,
                            "SteamRep Status",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Exclamation,
                            MessageBoxDefaultButton.Button1);
                        }
                    }
                }
            }
            catch
            {

            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            list_friendreq.Visible = !list_friendreq.Visible;
            if (list_friendreq.Visible)
            {
                ShrinkFriends();
            }
            else
            {
                GrowFriends();
            }
            list_friendreq.SetObjects(ListFriendRequests.Get());
        }

        private void friends_list_ItemActivate_1(object sender, EventArgs e)
        {
            bot.main.Invoke((Action)(() =>
            {
                if (friends_list.SelectedItem != null)
                {
                    ulong sid = Convert.ToUInt64(column_sid.GetValue(friends_list.SelectedItem.RowObject));
                    string selected = bot.SteamFriends.GetFriendPersonaName(sid);
                    if (!chat_opened)
                    {
                        chat = new Chat(bot);
                        chat.AddChat(selected, sid);
                        chat.Show();
                        chat.Focus();
                        chat_opened = true;
                    }
                    else
                    {
                        bool found = false;
                        foreach (TabPage tab in chat.ChatTabControl.TabPages)
                        {
                            if (tab.Text == selected)
                            {
                                chat.ChatTabControl.SelectedTab = tab;
                                chat.Focus();
                                found = true;
                            }
                        }
                        if (!found)
                        {
                            chat.AddChat(selected, sid);
                            chat.Focus();
                        }
                    }
                }
            }));
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            list_friendreq.SetObjects(ListFriendRequests.Get());
        }

        private void Friends_ResizeEnd(object sender, EventArgs e)
        {
            form_friendsHeight = friends_list.Height;
            form_friendreqHeight = list_friendreq.Height;
        }

        private void minimizeToTrayOnCloseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            minimizeToTray = minimizeToTrayOnCloseToolStripMenuItem.Checked;
        }

        private void text_search_Enter(object sender, EventArgs e)
        {
            if (text_search.Text == "Search")
            {
                text_search.Clear();
                text_search.Font = new Font(text_search.Font, FontStyle.Regular);
                text_search.ForeColor = SystemColors.WindowText;
            }
        }

        private void text_search_Leave(object sender, EventArgs e)
        {
            if (text_search.Text == "")
            {
                text_search.Font = new Font(text_search.Font, FontStyle.Italic);
                text_search.ForeColor = SystemColors.ControlDark;
                text_search.Text = "Search";
            }
        }

        private void text_search_TextChanged(object sender, EventArgs e)
        {
            if (text_search.Text == "")
                this.friends_list.SetObjects(ListFriends.Get());
            else
                this.friends_list.SetObjects(ListFriends.Get(text_search.Text));
        }
    }
}
