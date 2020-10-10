using System;
using System.Collections.Generic;
using KN_Loader;
using SyncMultiplayer;

namespace KN_Core {
  public abstract class BaseMod {
    public Core Core { get; }
    public string Name { get; }
    public int Id { get; }

    public int Version { get; }
    public int Patch { get; }
    public int ClientVersion { get; }

    public KnSkin Icon { get; }
    public KnSkin Icon { get; private set; }

    public BaseMod(Core core, string name, int id, int version, int patch, int clientVersion) {
      Core = core;
      Name = name;
      Id = id;
      Version = version;
      Patch = patch;
      ClientVersion = clientVersion;
    }

    public virtual void OnStart() { }
    public virtual void OnStop() { }

    public virtual void OnGUI(int id, Gui gui, ref float x, ref float y) { }

    public virtual void Update(int id) { }
    public virtual void LateUpdate(int id) { }
    public virtual void FixedUpdate(int id) { }

    public virtual void ResetState() { }
    public virtual void ResetPickers() { }

    public virtual bool WantsCaptureInput() {
      return true;
    }

    public virtual bool LockCameraRotation() {
      return false;
    }

    public virtual bool WantsHideUi() {
      return false;
    }

    public virtual void OnReloadAll() { }

    public virtual void OnUdpData(SmartfoxDataPackage data) { }

    public virtual void OnCarLoaded(KnCar car) { }

    protected void SetIcon(KnSkin icon) {
      Icon = icon;
    }
  }
}