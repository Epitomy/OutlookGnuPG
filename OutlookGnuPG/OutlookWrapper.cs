// This program is free software: you can redistribute it and/or modify
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
using System.Text;
using Outlook = Microsoft.Office.Interop.Outlook;

// Helper wrapper classes used to monitor items.
// See http://www.outlookcode.com/codedetail.aspx?id=1734
namespace OutlookGnuPG
{
  #region Base class wrapper
  /// <summary>
  /// Delegate signature to inform the application about disposing objects.
  /// </summary>
  /// <param name="id">The unique ID of the closed object.</param>
  /// <param name="o">The closed object.</param>
  public delegate void OutlookWrapperDisposeDelegate(Guid id, object o);

  /// <summary>
  /// The OutlookWrapper Class itself has a unique ID, the wrapped object and a closed event.
  /// </summary>
  internal abstract class OutlookWrapper
  {
    /// <summary>
    /// The event occurs when the monitored object is disposed.
    /// </summary>
    public event OutlookWrapperDisposeDelegate Dispose = null;

    /// <summary>
    /// The pointer to the wrapped object.
    /// </summary>
    private object _Object;
    public object Object { get { return _Object; } private set { _Object = value; } }

    /// <summary>
    /// The unique ID of the wrapped object.
    /// </summary>
    private Guid _Id;
    public Guid Id { get { return _Id; } private set { _Id = value; } }

    /// <summary>
    /// Handle the close of the wrapped object.
    /// </summary>
    protected void OnClosed()
    {
      if (Dispose != null) { Dispose(Id, Object); }
      Object = null;
    }

    /// <summary>
    /// The constructor (what else)
    /// </summary>
    /// <param name="o">The pointer to the object to monitor</param>
    public OutlookWrapper(object o)
    {
      Object = o;
      Id = Guid.NewGuid();
    }
  }
  #endregion

  #region Explorer wrapper
  /// <summary>
  /// Delegate signature to handle (some) explorer events.
  /// </summary>
  /// <param name="explorer">the explorer for which the event is fired</param>
  public delegate void ExplorerActivateDelegate(Outlook.Explorer explorer);
  public delegate void ExplorerDeactivateDelegate(Outlook.Explorer explorer);
  public delegate void ExplorerViewSwitchDelegate(Outlook.Explorer explorer);
  public delegate void ExplorerCloseDelegate(Outlook.Explorer exlorer);

  /// <summary>
  /// 
  /// </summary>
  internal class ExplorerWrapper : OutlookWrapper
  {
    /// <summary>
    /// Public exlorer events.
    /// </summary>
    public event ExplorerActivateDelegate Activate = null;
    public event ExplorerDeactivateDelegate Deactivate = null;
    public event ExplorerViewSwitchDelegate ViewSwitch = null;
    public event ExplorerCloseDelegate Close = null;

    public ExplorerWrapper(Outlook.Explorer explorer)
      : base(explorer)
    {
      ConnectEvents();
    }

    private void ConnectEvents()
    {
      Outlook.Explorer explorer = Object as Outlook.Explorer;
      ((Outlook.ExplorerEvents_10_Event)explorer).Activate += new Outlook.ExplorerEvents_10_ActivateEventHandler(ExplorerWrapper_Activate);
      ((Outlook.ExplorerEvents_10_Event)explorer).Deactivate += new Outlook.ExplorerEvents_10_DeactivateEventHandler(ExplorerWrapper_Deactivate);
      ((Outlook.ExplorerEvents_10_Event)explorer).ViewSwitch += new Outlook.ExplorerEvents_10_ViewSwitchEventHandler(ExplorerWrapper_ViewSwitch);
      ((Outlook.ExplorerEvents_10_Event)explorer).Close += new Outlook.ExplorerEvents_10_CloseEventHandler(ExplorerWrapper_Close);
    }

    void ExplorerWrapper_Close()
    {
      if (Close != null) { Close(Object as Outlook.Explorer); }
      DisconnectEvents();
      GC.Collect();
      GC.WaitForPendingFinalizers();
      OnClosed();
    }

    void ExplorerWrapper_ViewSwitch()
    {
      if (ViewSwitch != null) { ViewSwitch(Object as Outlook.Explorer); }
    }

    void ExplorerWrapper_Deactivate()
    {
      if (Deactivate != null) { Deactivate(Object as Outlook.Explorer); }
    }

    private void ExplorerWrapper_Activate()
    {
      if (Activate != null) { Activate(Object as Outlook.Explorer); }
    }

    private void DisconnectEvents()
    {
      Outlook.Explorer explorer = Object as Outlook.Explorer;
      ((Outlook.ExplorerEvents_10_Event)explorer).Activate -= new Outlook.ExplorerEvents_10_ActivateEventHandler(ExplorerWrapper_Activate);
      ((Outlook.ExplorerEvents_10_Event)explorer).Deactivate -= new Outlook.ExplorerEvents_10_DeactivateEventHandler(ExplorerWrapper_Deactivate);
      ((Outlook.ExplorerEvents_10_Event)explorer).ViewSwitch -= new Outlook.ExplorerEvents_10_ViewSwitchEventHandler(ExplorerWrapper_ViewSwitch);
      ((Outlook.ExplorerEvents_10_Event)explorer).Close -= new Outlook.ExplorerEvents_10_CloseEventHandler(ExplorerWrapper_Close);
    }
  }
  #endregion

  #region Inspector wrapper
  /// <summary>
  /// The wrapper class to warp an Inspector objet.
  /// </summary>
  internal class InspectorWrapper : OutlookWrapper
  {
    /// <summary>
    /// The constructor
    /// </summary>
    /// <param name="inspector">the inspector object to monitor</param>
    public InspectorWrapper(Outlook.Inspector inspector)
      : base(inspector)
    {
      ConnectEvents();
    }

    /// <summary>
    /// Connect inspector events, hookup the close event.
    /// </summary>
    private void ConnectEvents()
    {
      Outlook.Inspector inspector = Object as Outlook.Inspector;

      // Hookup inspector events
      ((Outlook.InspectorEvents_Event)inspector).Close += new Outlook.InspectorEvents_CloseEventHandler(InspectorWrapper_Close);
    }

    /// <summary>
    /// The close event handler fired when the inspector closes.
    /// </summary>
    void InspectorWrapper_Close()
    {
      DisconnectEvents();
      GC.Collect();
      GC.WaitForPendingFinalizers();
      OnClosed();
    }

    /// <summary>
    /// Disconnect inspector events, unhook close event.
    /// </summary>
    protected virtual void DisconnectEvents()
    {
      Outlook.Inspector inspector = Object as Outlook.Inspector;

      // Unhook events from the inspector
      ((Outlook.InspectorEvents_Event)inspector).Close -= new Outlook.InspectorEvents_CloseEventHandler(InspectorWrapper_Close);
    }
  }
  #endregion

  #region MailItem wrapper
  /// <summary>
  /// Delegate signature to handle (some) mailItem events.
  /// </summary>
  /// <param name="mailItem">the mailItem for which the event is fired</param>
  /// <param name="Cancel">False when the event occurs. If the event procedure sets this argument to True,
  /// the open operation is not completed and the inspector is not displayed.</param>
  public delegate void MailItemInspectorOpenDelegate(Outlook.MailItem mailItem, ref bool Cancel);
  public delegate void MailItemInspectorSaveDelegate(Outlook.MailItem mailItem, ref bool Cancel);
  public delegate void MailItemInspectorCloseDelegate(Outlook.MailItem mailItem, ref bool Cancel);

  /// <summary>
  /// The wrapper class to monitor a mailItem.
  /// </summary>
  internal class MailItemInspector : InspectorWrapper
  {
    /// <summary>
    /// Private member(s).
    /// </summary>
    private Outlook.MailItem _mailItem = null;

    /// <summary>
    /// Public mailItem events.
    /// </summary>
    public event MailItemInspectorOpenDelegate Open = null;
    public event MailItemInspectorSaveDelegate Save = null;
    public event MailItemInspectorCloseDelegate Close = null;

    /// <summary>
    /// The constructor to record the associate mailItem and register events.
    /// </summary>
    /// <param name="inspector"></param>
    public MailItemInspector(Outlook.Inspector inspector)
      : base(inspector)
    {
      _mailItem = inspector.CurrentItem as Outlook.MailItem;
      ConnectEvents();
    }

    /// <summary>
    /// Connect mailItem events, hookup the open, write and close events.
    /// </summary>
    private void ConnectEvents()
    {
      ((Outlook.ItemEvents_10_Event)_mailItem).Open += new Outlook.ItemEvents_10_OpenEventHandler(MailItemInspector_Open);
      ((Outlook.ItemEvents_10_Event)_mailItem).Close += new Outlook.ItemEvents_10_CloseEventHandler(MailItemInspector_Close);
      ((Outlook.ItemEvents_10_Event)_mailItem).Write += new Outlook.ItemEvents_10_WriteEventHandler(MailItemInspector_Write);
    }

    /// <summary>
    /// MailItem events: Open, Write and Close.
    /// Calls the registered application mailItem events.
    /// </summary>
    /// <param name="Cancel">False when the event occurs. If the event procedure sets this argument to True,
    /// the open operation is not completed and the inspector is not displayed.</param>
    private void MailItemInspector_Open(ref bool Cancel)
    {
      if (Open != null) Open(_mailItem, ref Cancel);
    }

    private void MailItemInspector_Write(ref bool Cancel)
    {
      if (Save != null) Save(_mailItem, ref Cancel);
    }

    private void MailItemInspector_Close(ref bool Cancel)
    {
      if (Close != null) Close(_mailItem, ref Cancel);
    }

    /// <summary>
    /// Disconnect mailItem events, unhook open, write and close events.
    /// </summary>
    protected override void DisconnectEvents()
    {
      ((Outlook.ItemEvents_10_Event)_mailItem).Open -= new Outlook.ItemEvents_10_OpenEventHandler(MailItemInspector_Open);
      ((Outlook.ItemEvents_10_Event)_mailItem).Close -= new Outlook.ItemEvents_10_CloseEventHandler(MailItemInspector_Close);
      ((Outlook.ItemEvents_10_Event)_mailItem).Write -= new Outlook.ItemEvents_10_WriteEventHandler(MailItemInspector_Write);

      base.DisconnectEvents();
    }
  }
  #endregion
}
