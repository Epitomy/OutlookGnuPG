﻿using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace OutlookGnuPG
{
  internal partial class About : Form
  {
    internal About()
    {
      InitializeComponent();
    }

    private string AssemblyTitle
    {
      get
      {
        object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyTitleAttribute), false);
        if (attributes.Length > 0)
        {
          AssemblyTitleAttribute titleAttribute = (AssemblyTitleAttribute)attributes[0];
          if (titleAttribute.Title != "")
          {
            return titleAttribute.Title;
          }
        }
        return Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().CodeBase);
      }
    }

    public Version AssemblyVersion
    {
      get { return Assembly.GetExecutingAssembly().GetName().Version; }
    }

    protected override void OnLoad(EventArgs e)
    {
      BlogLabel.Links.Add(0, BlogLabel.Text.Length, "http://" + BlogLabel.Text);
      DonateLabel.Links.Add(0, DonateLabel.Text.Length, "https://www.paypal.com/cgi-bin/webscr?cmd=_donations&business=PLFSJBFC5FG3Q&lc=BE&item_name=David%20Cumps&item_number=OutlookGnuPG&currency_code=EUR&bn=PP%2dDonationsBF%3abtn_donate_SM%2egif%3aNonHosted");
      IconLabel.Links.Add(0, IconLabel.Text.Length, "http://www.famfamfam.com/");
      OpenPGPLink.Links.Add(0, OpenPGPLink.Text.Length, "http://www.starksoft.com/");
      ClipboardLink.Links.Add(0, ClipboardLink.Text.Length, "http://www.codeproject.com/KB/system/clipboard_backup_cs.aspx?display=Print");
      ForkLabel.Links.Add(0, ForkLabel.Text.Length, "http://github.com/twalrant/OutlookGnuPG");

      Text = String.Format("About {0} ", AssemblyTitle);

      AboutLabel.Text = string.Format("{0} {1}.{2}.{3}.{4}",
                                      AssemblyTitle,
                                      AssemblyVersion.Major,
                                      AssemblyVersion.Minor,
                                      AssemblyVersion.Build,
                                      AssemblyVersion.Revision);
      DateTime buildDate =
         new FileInfo(Assembly.GetExecutingAssembly().Location).LastWriteTime;
      DateLabel.Text = buildDate.ToLongDateString();
      DateLabel.TextAlign = System.Drawing.ContentAlignment.TopRight;
    }

    private void ClickLink(object sender, LinkLabelLinkClickedEventArgs e)
    {
      e.Link.Visited = true;
      Process.Start(e.Link.LinkData.ToString());
    }
  }
}
