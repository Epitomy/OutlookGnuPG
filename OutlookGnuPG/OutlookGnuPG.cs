﻿// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or any
// later version.
//
// This program is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Text.RegularExpressions;

using OutlookGnuPG.Properties;

#if VS2008
using Microsoft.Office.Core;
using Microsoft.Office.Interop.Outlook;
#endif
using Outlook = Microsoft.Office.Interop.Outlook;
using Office = Microsoft.Office.Core;
using Microsoft.VisualStudio.Tools.Applications.Runtime;

using Starksoft.Cryptography.OpenPGP;
using Exception = System.Exception;

// TODO: Refactor some of the checks to central places

namespace OutlookGnuPG
{
  public partial class OutlookGnuPG
  {
    #region VSTO generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InternalStartup()
    {
      this.Startup += new System.EventHandler(OutlookGnuPG_Startup);
      this.Shutdown += new System.EventHandler(OutlookGnuPG_Shutdown);
    }

    #endregion

    private Properties.Settings _settings;
    private GnuPG _gnuPg;
    private PositionalCommandBar _gpgBar;
    private const string _gnuPgErrorString = "[@##$$##@|!GNUPGERROR!|@##$$##@]"; // Hacky way of dealing with exceptions
    private Outlook.Explorer _explorer;
    private Outlook.Explorers _explorers;
    private Outlook.Inspectors _inspectors;        // Outlook inspectors collection

    // This dictionary holds our Wrapped Inspectors, Explorers, MailItems
    private Dictionary<Guid, object> _WrappedObjects;

    // The GC comes along and eats our buttons, we need to hold a reference to it... *sigh*
    private IDictionary<string, Office.CommandBarButton> _buttons = new Dictionary<string, Office.CommandBarButton>();

    private void OutlookGnuPG_Startup(object sender, EventArgs e)
    {
      _settings = new Properties.Settings();

      if (string.IsNullOrEmpty(_settings.GnuPgPath))
      {
        _gnuPg = new GnuPG();
        Settings(); // Prompt for GnuPG Path
      }
      else
      {
        _gnuPg = new GnuPG(null, _settings.GnuPgPath);
        if (!_gnuPg.BinaryExists())
        {
          _settings.GnuPgPath = string.Empty;
          Settings(); // Prompt for GnuPG Path
        }
      }
      _gnuPg.OutputType = OutputTypes.AsciiArmor;

      _WrappedObjects = new Dictionary<Guid, object>();

      // Initialize command bar, starting with registering an Explorer Close Event.
      // See http://social.msdn.microsoft.com/Forums/en-US/vsto/thread/df53276b-6b44-448f-be86-7dd46c3786c7/
      _explorer = Application.ActiveExplorer();
      if (_explorer != null)
      {
        ((Outlook.ExplorerEvents_10_Event)_explorer).Close += new Outlook.ExplorerEvents_10_CloseEventHandler(OutlookGnuPG_CloseActiveExplorer);
        AddGnuPGCommandBar();
      }
      // Register an event for ItemSend
      Application.ItemSend += Application_ItemSend;
#if VSTO2008
      ((ApplicationEvents_11_Event)Application).Quit += OutlookGnuPG_Quit;
#endif

      // Initialize the outlook explorers
      _explorers = this.Application.Explorers;
      _explorers.NewExplorer += new Outlook.ExplorersEvents_NewExplorerEventHandler(OutlookGnuPG_NewExplorer);
      for (int i = _explorers.Count; i >= 1; i--)
      {
        WrapExplorer(_explorers[i]);
      }

      // Initialize the outlook inspectors
      _inspectors = this.Application.Inspectors;
      _inspectors.NewInspector += new Outlook.InspectorsEvents_NewInspectorEventHandler(OutlookGnuPG_NewInspector);
    }

    /// <summary>
    /// Event handler whenever the active explorer is closed.
    /// See http://social.msdn.microsoft.com/Forums/en-US/vsto/thread/df53276b-6b44-448f-be86-7dd46c3786c7/
    /// </summary>
    void OutlookGnuPG_CloseActiveExplorer()
    {
      _gpgBar.SavePosition(_settings);
    }

    /// <summary>
    /// Shutdown the Add-In.
    /// Note: some closing statements must happen before this event, see OutlookGnuPG_ExplorerClose().
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OutlookGnuPG_Shutdown(object sender, EventArgs e)
    {
      // Unhook event handler
      _inspectors.NewInspector -= new Outlook.InspectorsEvents_NewInspectorEventHandler(OutlookGnuPG_NewInspector);
      _explorers.NewExplorer -= new Outlook.ExplorersEvents_NewExplorerEventHandler(OutlookGnuPG_NewExplorer);
      if (_explorer != null)
      {
        ((Outlook.ExplorerEvents_10_Event)_explorer).Close -= new Outlook.ExplorerEvents_10_CloseEventHandler(OutlookGnuPG_CloseActiveExplorer);
      }

      _WrappedObjects.Clear();
      _WrappedObjects = null;
      _inspectors = null;
      _explorers = null;
      _explorer = null;
    }

    #region Explorer Logic
    /// <summary>
    /// The NewExplorer event fires whenever a new explorer is created. We use
    /// this event to toggle the visibility of the commandbar.
    /// </summary>
    /// <param name="explorer">the new created Explorer</param>
    void OutlookGnuPG_NewExplorer(Outlook.Explorer explorer)
    {
      WrapExplorer(explorer);
    }

    /// <summary>
    /// Wrap Explorer object to managed Explorer events.
    /// </summary>
    /// <param name="explorer">the outlook explorer to manage</param>
    private void WrapExplorer(Outlook.Explorer explorer)
    {
      if (_WrappedObjects.ContainsValue(explorer) == true)
        return;

      ExplorerWrapper wrappedExplorer = new ExplorerWrapper(explorer);
      wrappedExplorer.Dispose += new OutlookWrapperDisposeDelegate(ExplorerWrapper_Dispose);
      wrappedExplorer.ViewSwitch += new ExplorerViewSwitchDelegate(wrappedExplorer_ViewSwitch);
      _WrappedObjects[wrappedExplorer.Id] = explorer;
    }

    /// <summary>
    /// WrapEvent to dispose the wrappedExplorer
    /// </summary>
    /// <param name="id">the UID of the wrappedExplorer</param>
    /// <param name="o">the wrapped Explorer object</param>
    private void ExplorerWrapper_Dispose(Guid id, object o)
    {
      ExplorerWrapper wrappedExplorer = o as ExplorerWrapper;
      wrappedExplorer.Dispose -= new OutlookWrapperDisposeDelegate(ExplorerWrapper_Dispose);
      wrappedExplorer.ViewSwitch -= new ExplorerViewSwitchDelegate(wrappedExplorer_ViewSwitch);
      _WrappedObjects.Remove(id);
    }

    /// <summary>
    /// WrapEvent fired for ViewSwitch event.
    /// </summary>
    /// <param name="explorer">the explorer for which a switchview event fired.</param>
    void wrappedExplorer_ViewSwitch(Outlook.Explorer explorer)
    {
      Office.CommandBars bars = explorer.CommandBars;
      PositionalCommandBar gpgBar = GetGnuPGCommandBar(bars);
      if (gpgBar == null)
        return;
      if (explorer.CurrentFolder.DefaultMessageClass == "IPM.Note")
      {
        gpgBar.Bar.Visible = true;
      }
      else
      {
        gpgBar.Bar.Visible = false;
      }
    }
    #endregion

    #region Inspector Logic
    /// <summary>
    /// The NewInspector event fires whenever a new inspector is displayed. We use
    /// this event to initialize button to mail item inspectors.
    /// The inspector logic handles the registration and execution of mailItem
    /// events (Open, Close and Write) to initialize, maintain and save the
    /// ribbon button states per mailItem.
    /// </summary>
    /// <param name="Inspector">the new created Inspector</param>
    private void OutlookGnuPG_NewInspector(Outlook.Inspector inspector)
    {
      Outlook.MailItem mailItem = inspector.CurrentItem as Outlook.MailItem;
      if (mailItem != null && mailItem.Sent == false)
      {
        WrapMailItem(inspector);
      }
    }

    /// <summary>
    /// Wrap mailItem object to managed mailItem events.
    /// </summary>
    /// <param name="explorer">the outlook explorer to manage</param>
    private void WrapMailItem(Outlook.Inspector inspector)
    {
      if (_WrappedObjects.ContainsValue(inspector) == true)
        return;

      MailItemInspector wrappedMailItem = new MailItemInspector(inspector);
      wrappedMailItem.Dispose += new OutlookWrapperDisposeDelegate(MailItemInspector_Dispose);
      wrappedMailItem.Close += new MailItemInspectorCloseDelegate(mailItem_Close);
      wrappedMailItem.Open += new MailItemInspectorOpenDelegate(mailItem_Open);
      wrappedMailItem.Save += new MailItemInspectorSaveDelegate(mailItem_Save);
      _WrappedObjects[wrappedMailItem.Id] = inspector;
    }

    /// <summary>
    /// WrapEvent to dispose the wrappedMailItem
    /// </summary>
    /// <param name="id">the UID of the wrappedMailItem</param>
    /// <param name="o">the wrapped mailItem object</param>
    private void MailItemInspector_Dispose(Guid id, object o)
    {
      MailItemInspector wrappedMailItem = o as MailItemInspector;
      wrappedMailItem.Dispose -= new OutlookWrapperDisposeDelegate(MailItemInspector_Dispose);
      wrappedMailItem.Close -= new MailItemInspectorCloseDelegate(mailItem_Close);
      wrappedMailItem.Open -= new MailItemInspectorOpenDelegate(mailItem_Open);
      wrappedMailItem.Save -= new MailItemInspectorSaveDelegate(mailItem_Save);
      _WrappedObjects.Remove(id);
    }

    /// <summary>
    /// WrapperEvent fired when a mailItem is opened.
    /// This handler is designed to initialize the state of the compose button
    /// states (Sign/Encrypt) with recorded values, if present, or with default
    /// settings values.
    /// </summary>
    /// <param name="mailItem">the opened mailItem</param>
    /// <param name="Cancel">False when the event occurs. If the event procedure sets this argument to True,
    /// the open operation is not completed and the inspector is not displayed.</param>
    void mailItem_Open(Outlook.MailItem mailItem, ref bool Cancel)
    {
      // Only handle mail to be sent (in composing)
      if (mailItem != null && mailItem.Sent == true)
        return;

      Outlook.UserProperty SignProperpty = mailItem.UserProperties["GnuPGSetting.Sign"];
      if (SignProperpty == null)
      {
        ribbon.SignButton.Checked = _settings.AutoSign;
      }
      else
      {
        ribbon.SignButton.Checked = (bool)SignProperpty.Value;
      }

      Outlook.UserProperty EncryptProperpty = mailItem.UserProperties["GnuPGSetting.Encrypt"];
      if (EncryptProperpty == null)
      {
        ribbon.EncryptButton.Checked = _settings.AutoEncrypt;
      }
      else
      {
        ribbon.EncryptButton.Checked = (bool)EncryptProperpty.Value;
      }

      ribbon.InvalidateButtons();
    }

    /// <summary>
    /// WrapperEvent fired when a mailItem is closed.
    /// </summary>
    /// <param name="mailItem">the mailItem to close</param>
    /// <param name="Cancel">False when the event occurs. If the event procedure sets this argument to True,
    /// the open operation is not completed and the inspector is not displayed.</param>
    void mailItem_Close(Outlook.MailItem mailItem, ref bool Cancel)
    {
      // Only handle mail to be sent (in composing)
      if (mailItem != null && mailItem.Sent == true)
        return;

      bool toSave = false;
      Outlook.UserProperty SignProperpty = mailItem.UserProperties["GnuPGSetting.Sign"];
      if (SignProperpty == null || (bool)SignProperpty.Value != ribbon.SignButton.Checked)
      {
        toSave = true;
      }
      Outlook.UserProperty EncryptProperpty = mailItem.UserProperties["GnuPGSetting.Encrypt"];
      if (EncryptProperpty == null || (bool)EncryptProperpty.Value != ribbon.EncryptButton.Checked)
      {
        toSave = true;
      }
      if (toSave == true)
      {
#if DISABLED
        BoolEventArgs ev = e as BoolEventArgs;
        DialogResult res = MessageBox.Show("Do you want to save changes?",
                                           "OutlookGnuPG",
                                           MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation);
        if (res == DialogResult.Yes)
        {
          // Must call mailItem.Write event handler (mailItem_Save) explicitely as it is not always called
          // from the mailItem.Save() method. Mail is effectly saved only if a property changed.
          mailItem_Save(sender, EventArgs.Empty);
          mailItem.Save();
        }
        if (res == DialogResult.Cancel)
        {
          ev.Value = true;
        }
#else
        // Invalidate the mailItem to force Outlook to ask to save the mailItem, hence calling
        // the mailItem_Save() handler to record the buttons state.
        // Note: the reason (button state property change) to save the mailItem is not necessairy obvious
        // to the user, certainly if nothing has been updated/changed by the user. If specific notification
        // is required see DISABLED code above. Beware, it might open 2 dialog boxes: the add-in custom and
        // the regular Outlook save confirmation.
        mailItem.Subject = mailItem.Subject;
      }
#endif
    }

    /// <summary>
    /// WrapperEvent fired when a mailItem is saved.
    /// This handler is designed to record the compose button state (Sign/Encrypt)
    /// associated to this mailItem.
    /// </summary>
    /// <param name="mailItem">the mailItem to save</param>
    /// <param name="Cancel">False when the event occurs. If the event procedure sets this argument to True,
    /// the open operation is not completed and the inspector is not displayed.</param>
    void mailItem_Save(Outlook.MailItem mailItem, ref bool Cancel)
    {
      // Only handle mail to be sent (in composing)
      if (mailItem != null && mailItem.Sent == true)
        return;

      // Record compose button states.
      Outlook.UserProperty SignProperpty = mailItem.UserProperties["GnuPGSetting.Sign"];
      if (SignProperpty == null)
      {
        SignProperpty = mailItem.UserProperties.Add("GnuPGSetting.Sign", Outlook.OlUserPropertyType.olYesNo, false, null);
      }
      SignProperpty.Value = ribbon.SignButton.Checked;

      Outlook.UserProperty EncryptProperpty = mailItem.UserProperties["GnuPGSetting.Encrypt"];
      if (EncryptProperpty == null)
      {
        EncryptProperpty = mailItem.UserProperties.Add("GnuPGSetting.Encrypt", Outlook.OlUserPropertyType.olYesNo, false, null);
      }
      EncryptProperpty.Value = ribbon.EncryptButton.Checked;
    }
    #endregion

    #region CommandBar Logic
    private void AddGnuPGCommandBar()
    {
      // Add a commandbar with a verify/decrypt button
      Office.CommandBars bars = Application.ActiveExplorer().CommandBars;
      PositionalCommandBar gpgBar = GetGnuPGCommandBar(bars);

      // Add the bar if it doesn't exist yet
      if (gpgBar.Bar == null)
      {
        gpgBar = new PositionalCommandBar(bars.Add("GnuPGCommandBar", Type.Missing, Type.Missing, true));
        gpgBar.Bar.Protection = Office.MsoBarProtection.msoBarNoCustomize;
        gpgBar.Bar.Visible = true;
      }

      // Check if verify button exists, add it if it doesn't
      Office.CommandBarButton verifyButton = (Office.CommandBarButton)gpgBar.Bar.FindControl(Office.MsoControlType.msoControlButton, Type.Missing, "GnuPGVerifyMail", Type.Missing, true) ??
                                      (Office.CommandBarButton)gpgBar.Bar.Controls.Add(Office.MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);

      verifyButton.Style = Office.MsoButtonStyle.msoButtonIconAndCaption;
      verifyButton.Caption = "Verify";
      verifyButton.Tag = "GnuPGVerifyMail";
      verifyButton.Click += VerifyButton_Click;
      SetIcon(verifyButton, Resources.link_edit);
      if (!_buttons.ContainsKey(verifyButton.Tag))
        _buttons.Add(verifyButton.Tag, verifyButton);

      // Check if decrypt button exists, add it if it doesn't
      Office.CommandBarButton decryptButton = (Office.CommandBarButton)gpgBar.Bar.FindControl(Office.MsoControlType.msoControlButton, Type.Missing, "GnuPGDecryptMail", Type.Missing, true) ??
                                       (Office.CommandBarButton)gpgBar.Bar.Controls.Add(Office.MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);

      decryptButton.Style = Office.MsoButtonStyle.msoButtonIconAndCaption;
      decryptButton.Caption = "Decrypt";
      decryptButton.Tag = "GnuPGDecryptMail";
      decryptButton.Click += DecryptButton_Click;
      SetIcon(decryptButton, Resources.lock_edit);
      if (!_buttons.ContainsKey(decryptButton.Tag))
        _buttons.Add(decryptButton.Tag, decryptButton);

      // Check if about button exists, add it if it doesn't
      Office.CommandBarButton settingsButton = (Office.CommandBarButton)gpgBar.Bar.FindControl(Office.MsoControlType.msoControlButton, Type.Missing, "GnuPGSettings", Type.Missing, true) ??
                                        (Office.CommandBarButton)gpgBar.Bar.Controls.Add(Office.MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);

      settingsButton.Style = Office.MsoButtonStyle.msoButtonIconAndCaption;
      settingsButton.Caption = "Settings";
      settingsButton.Tag = "GnuPGSettings";
      settingsButton.Click += SettingsButton_Click;
      SetIcon(settingsButton, Resources.database_gear);
      if (!_buttons.ContainsKey(settingsButton.Tag))
        _buttons.Add(settingsButton.Tag, settingsButton);

      // Check if about button exists, add it if it doesn't
      Office.CommandBarButton aboutButton = (Office.CommandBarButton)gpgBar.Bar.FindControl(Office.MsoControlType.msoControlButton, Type.Missing, "AboutGnuPG", Type.Missing, true) ??
                                     (Office.CommandBarButton)gpgBar.Bar.Controls.Add(Office.MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);

      aboutButton.Style = Office.MsoButtonStyle.msoButtonIconAndCaption;
      aboutButton.Caption = "About";
      aboutButton.Tag = "AboutGnuPG";
      aboutButton.Click += AboutButton_Click;
      SetIcon(aboutButton, Resources.Logo);
      if (!_buttons.ContainsKey(aboutButton.Tag))
        _buttons.Add(aboutButton.Tag, aboutButton);

      gpgBar.RestorePosition(bars, _settings);
      _gpgBar = gpgBar;
    }

    private PositionalCommandBar GetGnuPGCommandBar(Office.CommandBars bars)
    {
      Office.CommandBar gpgBar = null;

      // Check if we added it already
      foreach (Office.CommandBar bar in bars)
      {
        if (((Office.CommandBar)bar).Name != "GnuPGCommandBar")
          continue;

        gpgBar = (Office.CommandBar)bar;
        break;
      }

      return new PositionalCommandBar(gpgBar);
    }

    private void SetIcon(Office.CommandBarButton buttonToSet, Bitmap iconToSet)
    {
      ReadOnlyCollection<DataClip> clipboardBackup = ClipboardHelper.GetClipboard();
      ClipboardHelper.EmptyClipboard();

      Clipboard.SetImage(iconToSet);
      buttonToSet.PasteFace();

      ClipboardHelper.EmptyClipboard();
      ClipboardHelper.SetClipboard(clipboardBackup);
    }

    private void VerifyButton_Click(Office.CommandBarButton Ctrl, ref bool CancelDefault)
    {
      // Get the selected item in Outlook and determine its type.
      Outlook.Selection outlookSelection = Application.ActiveExplorer().Selection;
      if (outlookSelection.Count <= 0)
        return;

      object selectedItem = outlookSelection[1];
      Outlook.MailItem mailItem = selectedItem as Outlook.MailItem;

      if (mailItem == null)
      {
        MessageBox.Show(
            "OutlookGnuPG can only verify mails.",
            "Invalid Item Type",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        return;
      }

      VerifyEmail(mailItem);
    }

    private void DecryptButton_Click(Office.CommandBarButton Ctrl, ref bool CancelDefault)
    {
      // Get the selected item in Outlook and determine its type.
      Outlook.Selection outlookSelection = Application.ActiveExplorer().Selection;
      if (outlookSelection.Count <= 0)
        return;

      object selectedItem = outlookSelection[1];
      Outlook.MailItem mailItem = selectedItem as Outlook.MailItem;

      if (mailItem == null)
      {
        MessageBox.Show(
            "OutlookGnuPG can only decrypt mails.",
            "Invalid Item Type",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        return;
      }

      DecryptEmail(mailItem);
    }

    private void AboutButton_Click(Office.CommandBarButton Ctrl, ref bool CancelDefault)
    {
      Globals.OutlookGnuPG.About();
    }

    private void SettingsButton_Click(Office.CommandBarButton Ctrl, ref bool CancelDefault)
    {
      Globals.OutlookGnuPG.Settings();
    }
    #endregion

    #region Send Logic
    private void Application_ItemSend(object Item, ref bool Cancel)
    {
      Outlook.MailItem mailItem = Item as Outlook.MailItem;

      if (mailItem == null)
        return;

#if VS2008
      //var inspector = Application.ActiveInspector();
      var inspector = mailItem.GetInspector;
      var currentRibbons = Globals.Ribbons[inspector];
      var currentRibbon = currentRibbons.GnuPGRibbonCompose;
#else
      GnuPGRibbon currentRibbon = ribbon;
#endif

      if (currentRibbon == null)
        return;

      string mail = mailItem.Body;
      Outlook.OlBodyFormat mailType = mailItem.BodyFormat;
      bool needToEncrypt = currentRibbon.EncryptButton.Checked;
      bool needToSign = currentRibbon.SignButton.Checked;

      // Early out when we don't need to sign/encrypt
      if (!needToEncrypt && !needToSign)
        return;

      if (mailType != Outlook.OlBodyFormat.olFormatPlain)
      {
        MessageBox.Show(
            "OutlookGnuPG can only sign/encrypt plain text mails. Please change the format, or disable signing/encrypting for this mail.",
            "Invalid Mail Format",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        Cancel = true; // Prevent sending the mail
        return;
      }

      // Still no gpg.exe path... Annoy the user once again, maybe he'll get it ;)
      if (string.IsNullOrEmpty(_settings.GnuPgPath))
        Settings();

      // Stubborn, give up
      if (string.IsNullOrEmpty(_settings.GnuPgPath))
      {
        MessageBox.Show(
            "OutlookGnuPG can only sign/encrypt when you provide a valid gpg.exe path. Please open Settings and configure it.",
            "Invalid GnuPG Executable",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        Cancel = true; // Prevent sending the mail
        return;
      }

      string passphrase = string.Empty;
      string privateKey = string.Empty;
      if (needToSign)
      {
        // Popup UI to select the passphrase and private key.
        Passphrase passphraseDialog = new Passphrase(_settings.DefaultKey, "Sign");
        DialogResult passphraseResult = passphraseDialog.ShowDialog();
        if (passphraseResult != DialogResult.OK)
        {
          // The user closed the passphrase dialog, prevent sending the mail
          Cancel = true;
          return;
        }

        passphrase = passphraseDialog.EnteredPassphrase;
        privateKey = passphraseDialog.SelectedKey;
        passphraseDialog.Close();

        if (string.IsNullOrEmpty(privateKey))
        {
          MessageBox.Show(
              "OutlookGnuPG needs a private key for signing. No keys were detected.",
              "Invalid Private Key",
              MessageBoxButtons.OK,
              MessageBoxIcon.Error);

          Cancel = true; // Prevent sending the mail
          return;
        }
      }

#if VS2008
      IList<string> recipients = new List<string> { string.Empty };
#else
      IList<string> recipients = new List<string>();
      recipients.Add(string.Empty);
#endif
      if (needToEncrypt)
      {
        // Popup UI to select the encryption targets 
        List<string> mailRecipients = new List<string>();
        foreach (Outlook.Recipient mailRecipient in mailItem.Recipients)
          mailRecipients.Add(GetAddressCN(((Outlook.Recipient)mailRecipient).Address));

        Recipient recipientDialog = new Recipient(mailRecipients); // Passing in the first addres, maybe it matches
        DialogResult recipientResult = recipientDialog.ShowDialog();

        if (recipientResult != DialogResult.OK)
        {
          // The user closed the recipient dialog, prevent sending the mail
          Cancel = true;
          return;
        }

        recipients = recipientDialog.SelectedKeys;
        recipientDialog.Close();

        if (recipients.Count == 0)
        {
          MessageBox.Show(
              "OutlookGnuPG needs a recipient when encrypting. No keys were detected/selected.",
              "Invalid Recipient Key",
              MessageBoxButtons.OK,
              MessageBoxIcon.Error);

          Cancel = true; // Prevent sending the mail
          return;
        }
      }

      // Sign and encrypt the plaintext mail
      if ((needToSign) && (needToEncrypt))
      {
        mail = SignAndEncryptEmail(mail, privateKey, passphrase, recipients);
      }
      else if (needToSign)
      {
        // Sign the plaintext mail if needed
        mail = SignEmail(mail, privateKey, passphrase);
      }
      else if (needToEncrypt)
      {
        // Encrypt the plaintext mail if needed
        mail = EncryptEmail(mail, passphrase, recipients);
      }

      // Update the new content
      if (mail != _gnuPgErrorString)
        mailItem.Body = mail;
      else
        Cancel = true;
    }

    private string SignEmail(string mail, string key, string passphrase)
    {
      using (MemoryStream inputStream = new MemoryStream(mail.Length))
      using (MemoryStream outputStream = new MemoryStream())
      {
        using (StreamWriter writer = new StreamWriter(inputStream))
        {
          writer.Write(mail);
          writer.Flush();
          inputStream.Position = 0;
          _gnuPg.Passphrase = passphrase;
          _gnuPg.Sender = key;

          try
          {
            _gnuPg.OutputStatus = false;
            _gnuPg.Sign(inputStream, outputStream);
          }
          catch (Exception ex)
          {
            MessageBox.Show(
                ex.Message,
                "GnuPG Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return _gnuPgErrorString;
          }
        }

        using (StreamReader reader = new StreamReader(outputStream))
        {
          outputStream.Position = 0;
          mail = reader.ReadToEnd();
        }
      }

      return mail;
    }

    private string EncryptEmail(string mail, string passphrase, IList<string> recipients)
    {
      using (MemoryStream inputStream = new MemoryStream(mail.Length))
      using (MemoryStream outputStream = new MemoryStream())
      {
        using (StreamWriter writer = new StreamWriter(inputStream))
        {
          // Ready for two passes encryption.
          foreach (string option in new string[] { "", "--trust-model always" })
          {
            _gnuPg.UserCmdOptions = option;

            if (_settings.Encrypt2Self == true)
              _gnuPg.UserCmdOptions += " --encrypt-to " + _settings.DefaultKey;

            writer.Write(mail);
            writer.Flush();
            inputStream.Position = 0;
            _gnuPg.Passphrase = passphrase;
            _gnuPg.Recipients = recipients;
            _gnuPg.OutputStatus = false;

            try
            {
              _gnuPg.Encrypt(inputStream, outputStream);
              break; // Stop two passes here on success.
            }
            catch (Exception ex)
            {
              if (string.IsNullOrEmpty(option) && ex.Message.StartsWith("gpg: C4771111"))
              {
                DialogResult res = MessageBox.Show(
                    ex.Message + Environment.NewLine + Environment.NewLine + "Encrypt mail anyway?",
                    "GnuPG Warning",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Exclamation);
                if (res == DialogResult.Cancel)
                  return _gnuPgErrorString;
              }
              else
              {
                MessageBox.Show(
                ex.Message,
                  "GnuPG Error",
                  MessageBoxButtons.OK,
                  MessageBoxIcon.Error);

                return _gnuPgErrorString;
              }
            }
            finally
            {
              _gnuPg.UserCmdOptions = string.Empty;
            }
          }
        }

        using (StreamReader reader = new StreamReader(outputStream))
        {
          outputStream.Position = 0;
          mail = reader.ReadToEnd();
        }
      }

      return mail;
    }

    private string SignAndEncryptEmail(string mail, string key, string passphrase, IList<string> recipients)
    {
      using (MemoryStream inputStream = new MemoryStream(mail.Length))
      using (MemoryStream outputStream = new MemoryStream())
      {
        using (StreamWriter writer = new StreamWriter(inputStream))
        {
          // Ready for two passes sign/encryption.
          foreach (string option in new string[] { "", "--trust-model always" })
          {
            _gnuPg.UserCmdOptions = option;

            if (_settings.Encrypt2Self == true)
              _gnuPg.UserCmdOptions += " --encrypt-to " + _settings.DefaultKey;

            writer.Write(mail);
            writer.Flush();
            inputStream.Position = 0;
            _gnuPg.Passphrase = passphrase;
            _gnuPg.Recipients = recipients;
            _gnuPg.Sender = key;
            _gnuPg.OutputStatus = false;

            try
            {
              _gnuPg.SignAndEncrypt(inputStream, outputStream);
              break; // Stop two passes here on success.
            }
            catch (Exception ex)
            {
              if (string.IsNullOrEmpty(option) && ex.Message.StartsWith("gpg: C4771111"))
              {
                DialogResult res = MessageBox.Show(
                  ex.Message + Environment.NewLine + Environment.NewLine + "Sign and Encrypt the mail anyway?",
                  "GnuPG Warning",
                  MessageBoxButtons.OKCancel,
                  MessageBoxIcon.Exclamation);
                if (res == DialogResult.Cancel)
                  return _gnuPgErrorString;
              }
              else
              {
                MessageBox.Show(
                    ex.Message,
                    "GnuPG Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return _gnuPgErrorString;
              }
            }
            finally
            {
              _gnuPg.UserCmdOptions = string.Empty;
            }
          }
        }

        using (StreamReader reader = new StreamReader(outputStream))
        {
          outputStream.Position = 0;
          mail = reader.ReadToEnd();
        }
      }

      return mail;
    }
    #endregion

    #region Receive Logic
    internal void VerifyEmail(Outlook.MailItem mailItem)
    {
      string mail = mailItem.Body;
      Outlook.OlBodyFormat mailType = mailItem.BodyFormat;

      if (mailType != Outlook.OlBodyFormat.olFormatPlain)
      {
        MessageBox.Show(
            "OutlookGnuPG can only verify plain text mails.",
            "Invalid Mail Format",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        return;
      }

      // Still no gpg.exe path... Annoy the user once again, maybe he'll get it ;)
      if (string.IsNullOrEmpty(_settings.GnuPgPath))
        Settings();

      // Stubborn, give up
      if (string.IsNullOrEmpty(_settings.GnuPgPath))
      {
        MessageBox.Show(
            "OutlookGnuPG can only verify when you provide a valid gpg.exe path. Please open Settings and configure it.",
            "Invalid GnuPG Executable",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        return;
      }

      string verifyResult = string.Empty;
      string errorResult = string.Empty;
      using (MemoryStream inputStream = new MemoryStream(mail.Length))
      using (MemoryStream outputStream = new MemoryStream())
      using (MemoryStream errorStream = new MemoryStream())
      {
        using (StreamWriter writer = new StreamWriter(inputStream))
        {
          writer.Write(mail);
          writer.Flush();
          inputStream.Position = 0;

          try
          {
            _gnuPg.OutputStatus = true;
            _gnuPg.Verify(inputStream, outputStream, errorStream);
          }
          catch (Exception ex)
          {
            string error = ex.Message;

            // We deal with bad signature later
            if (!error.ToLowerInvariant().Contains("bad signature"))
            {
              MessageBox.Show(
                  error,
                  "GnuPG Error",
                  MessageBoxButtons.OK,
                  MessageBoxIcon.Error);

              return;
            }
          }
        }

        using (StreamReader reader = new StreamReader(outputStream))
        {
          outputStream.Position = 0;
          verifyResult = reader.ReadToEnd();
        }

        using (StreamReader reader = new StreamReader(errorStream))
        {
          errorStream.Position = 0;
          errorResult = reader.ReadToEnd();
        }
      }

      if (verifyResult.Contains("BADSIG"))
      {
        errorResult = RemoveInvalidAka(errorResult.Replace("gpg: ", string.Empty));

        MessageBox.Show(
            errorResult,
            "Invalid Signature",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
      }
      else if (verifyResult.Contains("GOODSIG"))
      {
        errorResult = RemoveInvalidAka(errorResult.Replace("gpg: ", string.Empty));

        MessageBox.Show(
            errorResult,
            "Valid Signature",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
      }
      else
      {
        errorResult = RemoveInvalidAka(errorResult.Replace("gpg: ", string.Empty));

        MessageBox.Show(
            errorResult,
            "Unknown Signature",
            MessageBoxButtons.OK,
            MessageBoxIcon.Exclamation);
      }
    }

    internal void DecryptEmail(Outlook.MailItem mailItem)
    {
      string mail = mailItem.Body;
      Outlook.OlBodyFormat mailType = mailItem.BodyFormat;

      if (mailType != Outlook.OlBodyFormat.olFormatPlain)
      {
        MessageBox.Show(
            "OutlookGnuPG can only decrypt plain text mails.",
            "Invalid Mail Format",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        return;
      }

      // Still no gpg.exe path... Annoy the user once again, maybe he'll get it ;)
      if (string.IsNullOrEmpty(_settings.GnuPgPath))
        Settings();

      // Stubborn, give up
      if (string.IsNullOrEmpty(_settings.GnuPgPath))
      {
        MessageBox.Show(
            "OutlookGnuPG can only decrypt when you provide a valid gpg.exe path. Please open Settings and configure it.",
            "Invalid GnuPG Executable",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        return;
      }

      string passphrase = string.Empty;
      string privateKey = string.Empty;

      // Popup UI to select the passphrase and private key.
      Passphrase passphraseDialog = new Passphrase(_settings.DefaultKey, "Decrypt");
      DialogResult passphraseResult = passphraseDialog.ShowDialog();
      if (passphraseResult != DialogResult.OK)
      {
        // The user closed the passphrase dialog, prevent sending the mail
        return;
      }

      passphrase = passphraseDialog.EnteredPassphrase;
      privateKey = passphraseDialog.SelectedKey;
      passphraseDialog.Close();

      if (string.IsNullOrEmpty(privateKey))
      {
        MessageBox.Show(
            "OutlookGnuPG needs a private key for decrypting. No keys were detected.",
            "Invalid Private Key",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        return;
      }

      // Decrypt without fd-status (might already blow up, early out)
      // Decrypt with fd-status and cut out the stdout of normal decrypt (prevents BAD/GOODMC messages in message confusing us)
      string stdOutResult = string.Empty;
      using (MemoryStream inputStream = new MemoryStream(mail.Length))
      using (MemoryStream outputStream = new MemoryStream())
      {
        using (StreamWriter writer = new StreamWriter(inputStream))
        {
          writer.Write(mail);
          writer.Flush();
          inputStream.Position = 0;

          try
          {
            _gnuPg.OutputStatus = false;
            _gnuPg.Passphrase = passphrase;
            _gnuPg.Decrypt(inputStream, outputStream, new MemoryStream());
          }
          catch (Exception ex)
          {
            string error = ex.Message;

            // We deal with bad signature later
            if (!error.ToLowerInvariant().Contains("bad signature"))
            {
              MessageBox.Show(
                  error,
                  "GnuPG Error",
                  MessageBoxButtons.OK,
                  MessageBoxIcon.Error);

              return;
            }
          }
        }

        using (StreamReader reader = new StreamReader(outputStream))
        {
          outputStream.Position = 0;
          stdOutResult = reader.ReadToEnd();
        }
      }

      string verifyResult = string.Empty;
      string errorResult = string.Empty;
      using (MemoryStream inputStream = new MemoryStream(mail.Length))
      using (MemoryStream outputStream = new MemoryStream())
      using (MemoryStream errorStream = new MemoryStream())
      {
        using (StreamWriter writer = new StreamWriter(inputStream))
        {
          writer.Write(mail);
          writer.Flush();
          inputStream.Position = 0;

          try
          {
            _gnuPg.OutputStatus = true;
            _gnuPg.Passphrase = passphrase;
            _gnuPg.Decrypt(inputStream, outputStream, errorStream);
          }
          catch (Exception ex)
          {
            string error = ex.Message;

            // We deal with bad signature later
            if (!error.ToLowerInvariant().Contains("bad signature"))
            {
              MessageBox.Show(
                  error,
                  "GnuPG Error",
                  MessageBoxButtons.OK,
                  MessageBoxIcon.Error);

              return;
            }
          }
        }

        using (StreamReader reader = new StreamReader(outputStream))
        {
          outputStream.Position = 0;
          verifyResult = reader.ReadToEnd();
        }

        using (StreamReader reader = new StreamReader(errorStream))
        {
          errorStream.Position = 0;
          errorResult = reader.ReadToEnd();
        }
      }

      verifyResult = verifyResult.Replace(stdOutResult, string.Empty);

      // Verify: status-fd
      // stdOut: the message
      // error: gpg error/status

      if (verifyResult.Contains("BADMDC"))
      {
        errorResult = RemoveInvalidAka(errorResult.Replace("gpg: ", string.Empty));

        MessageBox.Show(
            errorResult,
            "Invalid Encryption",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
      }
      else if (verifyResult.Contains("GOODMDC"))
      {
        // Decrypted OK, check for validsig
        if (verifyResult.Contains("BADSIG"))
        {
          errorResult = RemoveInvalidAka(errorResult.Replace("gpg: ", string.Empty));

          MessageBox.Show(
              errorResult,
              "Invalid Signature",
              MessageBoxButtons.OK,
              MessageBoxIcon.Error);
        }
        else if (verifyResult.Contains("GOODSIG"))
        {
          errorResult = RemoveInvalidAka(errorResult.Replace("gpg: ", string.Empty));

          MessageBox.Show(
              errorResult,
              "Valid Signature",
              MessageBoxButtons.OK,
              MessageBoxIcon.Information);

          // Valid signature!
          mailItem.Body = stdOutResult;
        }
        else
        {
          // No signature?
          mailItem.Body = stdOutResult;
        }
      }
      else
      {
        errorResult = RemoveInvalidAka(errorResult.Replace("gpg: ", string.Empty));
        errorResult = errorResult.Replace("WARNING", Environment.NewLine + "WARNING");

        DialogResult res = MessageBox.Show(
            errorResult + Environment.NewLine + "Decrypt mail anyway?",
            "Unknown Encryption",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Exclamation);
        if (res == DialogResult.OK)
          mailItem.Body = stdOutResult;
      }
    }
    #endregion

    #region General Logic
    internal void About()
    {
      About aboutBox = new About();
      aboutBox.ShowDialog();
    }

    internal void Settings()
    {
      Settings settingsBox = new Settings(_settings);
      DialogResult result = settingsBox.ShowDialog();

      if (result != DialogResult.OK)
        return;

      _settings.GnuPgPath = settingsBox.GnuPgPath;
      _settings.Encrypt2Self = settingsBox.Encrypt2Self;
      _settings.AutoDecrypt = settingsBox.AutoDecrypt;
      _settings.AutoVerify = settingsBox.AutoVerify;
      _settings.AutoEncrypt = settingsBox.AutoEncrypt;
      _settings.AutoSign = settingsBox.AutoSign;
      _settings.DefaultKey = settingsBox.DefaultKey;
      _settings.DefaultDomain = settingsBox.DefaultDomain;
      _settings.Save();

      _gnuPg.BinaryPath = _settings.GnuPgPath;
    }

    #endregion

    #region Key Management
    internal IList<GnuKey> GetPrivateKeys()
    {
      GnuPGKeyCollection privateKeys = _gnuPg.GetSecretKeys();

      List<GnuKey> keys = new List<GnuKey>();
      foreach (GnuPGKey privateKey in privateKeys)
      {
#if VS2008
        keys.Add(new GnuKey
        {
          Key = privateKey.UserId,
          KeyDisplay = string.Format("{0} <{1}>", privateKey.UserName, privateKey.UserId)
        });
#else
        GnuKey k = new GnuKey();
        k.Key = privateKey.UserId;
        k.KeyDisplay = string.Format("{0} <{1}>", privateKey.UserName, privateKey.UserId);
        keys.Add(k);
#endif
      }

      return keys;
    }

    internal IList<GnuKey> GetPrivateKeys(string gnuPgPath)
    {
      _gnuPg.BinaryPath = gnuPgPath;
      if ( _gnuPg.BinaryExists() )
        return GetPrivateKeys();
      else
        return new List<GnuKey>();
    }

    public IList<GnuKey> GetKeys()
    {
      GnuPGKeyCollection privateKeys = _gnuPg.GetKeys();

      List<GnuKey> keys = new List<GnuKey>();
      foreach (GnuPGKey privateKey in privateKeys)
      {
#if VS2008
        keys.Add(new GnuKey
        {
          Key = privateKey.UserId,
          KeyDisplay = string.Format("{0} <{1}>", privateKey.UserName, privateKey.UserId)
        });
#else
        GnuKey k = new GnuKey();
        k.Key = privateKey.UserId;
        k.KeyDisplay = string.Format("{0} <{1}>", privateKey.UserName, privateKey.UserId);
        keys.Add(k);
#endif
      }

      return keys;
    }

    string GetAddressCN(string AddressX400)
    {
      char[] delimiters = { '/' };
      string[] splitAddress = AddressX400.Split(delimiters);
      for (int k = 0; k < splitAddress.Length; k++)
      {
        if (splitAddress[k].StartsWith("cn=", true, null) && !Regex.IsMatch(splitAddress[k], "ecipient", RegexOptions.IgnoreCase))
        {
          string address = Regex.Replace(splitAddress[k], "cn=", string.Empty, RegexOptions.IgnoreCase);
          if (!string.IsNullOrEmpty(_settings.DefaultDomain))
          {
            address += "@" + _settings.DefaultDomain;
            address = address.Replace("@@", "@");
          }
          return address;
        }
      }
      return AddressX400;
    }

    #endregion

    #region Helper Logic
    private string RemoveInvalidAka(string msg)
    {
      char[] delimiters = { '\r', '\n' };
      string result = string.Empty;
      Regex r = new Regex("aka.*jpeg image of size");
      foreach (string s in msg.Split(delimiters))
      {
        if (string.IsNullOrEmpty(s) || r.IsMatch(s))
          continue;
        result += s + Environment.NewLine;
      }
      return result;
    }

    public bool ValidateGnuPath(string gnuPath)
    {
      if (_gnuPg != null)
        return _gnuPg.BinaryExists(gnuPath);
      else
        return false;
    }
    #endregion
  }
}
