﻿using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace CC.OutlookGnuPG
{
    internal partial class Settings : Form
    {
        private readonly int _originalExeWidth;

        internal Settings(Properties.Settings settings)
        {
            InitializeComponent();

            AutoDecrypt = settings.AutoDecrypt;
            AutoVerify = settings.AutoVerify;
            AutoEncrypt = settings.AutoEncrypt;
            AutoSign = settings.AutoSign;

            DefaultKey = settings.DefaultKey;
            GnuPgPath = settings.GnuPgPath;
            _originalExeWidth = GnuPgExe.Width;

            // Temporary disable all settings regarding auto-verify/decrypt
            ComposeSettings.TabPages.RemoveByKey(ReadTab.Name);
        }

        internal string DefaultKey { get; private set; }

        internal bool AutoDecrypt
        {
            get { return DecryptCheckBox.Checked; }
            private set { DecryptCheckBox.Checked = value; }
        }

        internal bool AutoVerify
        {
            get { return VerifyCheckBox.Checked; }
            private set { VerifyCheckBox.Checked = value; }
        }

        internal bool AutoEncrypt
        {
            get { return EncryptCheckBox.Checked; }
            private set { EncryptCheckBox.Checked = value; }
        }

        internal bool AutoSign
        {
            get { return SignCheckBox.Checked; }
            private set { SignCheckBox.Checked = value; }
        }

        internal string GnuPgPath
        {
            get { return GnuPgExe.Text; }
            private set
            {
                GnuPgExe.Text = value;

                if (string.IsNullOrEmpty(value))
                {
                    ComposeTab.Enabled = ReadTab.Enabled = false;
                    ComposeSettings.TabPages.RemoveByKey(ComposeTab.Name);
                    //ComposeSettings.TabPages.RemoveByKey(ReadTab.Name);
                }
                else
                {
                    ComposeTab.Enabled = ReadTab.Enabled = true;

                    if (!ComposeSettings.TabPages.ContainsKey(ComposeTab.Name))
                        ComposeSettings.TabPages.Add(ComposeTab);

                    //if (!ComposeSettings.TabPages.ContainsKey(ReadTab.Name))
                    //    ComposeSettings.TabPages.Add(ReadTab);
                }

                PopulatePrivateKeys(!string.IsNullOrEmpty(value));
            }
        }

        private void BrowseButton_Click(object sender, System.EventArgs e)
        {
            if (!string.IsNullOrEmpty(GnuPgPath))
                GnuPgExeFolderDialog.SelectedPath = GnuPgPath;

            var result = GnuPgExeFolderDialog.ShowDialog(this);
            if (result != DialogResult.OK) 
                return;

            GnuPgPath = GnuPgExeFolderDialog.SelectedPath;
            ValidateGnuPath();
        }

        private void PopulatePrivateKeys(bool gotGnu)
        {
            IList<GnuKey> keys = gotGnu ? Globals.OutlookGnuPG.GetPrivateKeys(GnuPgPath) : new List<GnuKey>();

            KeyBox.DataSource = keys;
            KeyBox.DisplayMember = "KeyDisplay";
            KeyBox.ValueMember = "Key";

            if (KeyBox.Items.Count <= 0)
                return;

            KeyBox.SelectedValue = DefaultKey;

            // Enlarge dialog to fit the longest key
            using (var g = CreateGraphics())
            {
                var maxSize = Width;
                foreach (var key in keys)
                {
                    var textWidth = (int)g.MeasureString(key.KeyDisplay, KeyBox.Font).Width + 50 + 27;
                    if (textWidth > maxSize)
                        maxSize = textWidth;
                }
                Width = maxSize;
                CenterToScreen();
            }
        }

        private void OkButton_Click(object sender, System.EventArgs e)
        {
            if (!ValidateGnuPath()) 
                return;

            DialogResult = DialogResult.OK;
            Close();
        }

        private bool ValidateGnuPath()
        {
            if (string.IsNullOrEmpty(GnuPgPath))
            {
                // No GnuPath provided, complain!
                Errors.SetError(GnuPgExe, "No GnuPG provided!");
                GnuPgExe.Dock = DockStyle.None;
                GnuPgExe.Width = _originalExeWidth - 17;
                return false;
            }

            if (!File.Exists(Path.Combine(GnuPgPath, "gpg.exe")))
            {
                // No gpg.exe found, complain!
                Errors.SetError(GnuPgExe, "No gpg.exe found in directory!");
                GnuPgExe.Dock = DockStyle.None;
                GnuPgExe.Width = _originalExeWidth - 17;
                return false; 
            }

            // All fine
            Errors.SetError(GnuPgExe, string.Empty);
            GnuPgExe.Dock = DockStyle.Fill;
            GnuPgExe.Width = _originalExeWidth;

            DefaultKey = (KeyBox.Items.Count > 0) 
                ? ((KeyBox.SelectedValue != null) ? KeyBox.SelectedValue.ToString() : string.Empty) 
                : string.Empty;

            return true;
        }
    }
}
