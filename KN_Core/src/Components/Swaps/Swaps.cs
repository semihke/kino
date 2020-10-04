using System.Collections.Generic;
using System.Linq;
using CarX;
using KN_Loader;
using SyncMultiplayer;
using UnityEngine;

namespace KN_Core {
  public class Swaps {
    public bool Active => swapsEnabled_ && dataLoaded_;
    private readonly List<EngineData> engines_;
    private readonly List<EngineBalance> balance_;

    private readonly List<SwapData> allData_;

    private SwapData currentSwap_;
    private string currentSound_;

    private string defaultSoundId_;
    private float defaultFinalDrive_;
    private float defaultClutch_;
    private readonly CarDesc.Engine defaultEngine_;

    private float carListScrollH_;
    private Vector2 carListScroll_;

    private bool swapsEnabled_;
    private bool shouldRequestSwaps_;

    private readonly bool dataLoaded_;

    private readonly Core core_;

    public Swaps(Core core) {
      core_ = core;

      shouldRequestSwaps_ = true;

      engines_ = new List<EngineData>();
      balance_ = new List<EngineBalance>();
      dataLoaded_ = SwapsLoader.LoadData(ref engines_, ref balance_);
      if (!dataLoaded_) {
        return;
      }

      Log.Write($"[KN_Core::Swaps]: Swaps data successfully loaded from remote, engines: {engines_.Count}, balance: {balance_.Count}");

      allData_ = new List<SwapData>();
      defaultEngine_ = new CarDesc.Engine();
    }

    public void OnInit() {
      if (!dataLoaded_) {
        return;
      }

      if (DataSerializer.Deserialize<SwapData>("KN_Swaps", KnConfig.BaseDir + SwapData.ConfigFile, out var data)) {
        Log.Write($"[KN_Core::Swaps]: User swap data loaded, items: {data.Count}");
        allData_.AddRange(data.ConvertAll(d => (SwapData) d));
      }
    }

    public void OnDeinit() {
      if (!Active) {
        return;
      }

      DataSerializer.Serialize("KN_Swaps", allData_.ToList<ISerializable>(), KnConfig.BaseDir + SwapData.ConfigFile, Core.Version);
    }

    public void OnCarLoaded() {
      if (!Active) {
        return;
      }
    }

    public void Update() {
      if (shouldRequestSwaps_) {
        var status = AccessValidator.IsGranted(4, "KN_Swaps");
        if (status != AccessValidator.Status.Loading) {
          shouldRequestSwaps_ = false;
        }
        if (status == AccessValidator.Status.Granted) {
          swapsEnabled_ = true;
        }
      }

      if (!Active) {
        return;
      }

      if (core_.IsCarChanged) {
        defaultSoundId_ = core_.PlayerCar.Base.metaInfo.name;
        defaultFinalDrive_ = core_.PlayerCar.CarX.finaldrive;
        defaultClutch_ = core_.PlayerCar.CarX.clutchMaxTorque;

        var desc = core_.PlayerCar.Base.GetDesc();
        CopyEngine(desc.carXDesc.engine, defaultEngine_);
        Log.Write($"[KN_Core::Swaps]: Car changed storing default engine");

        FindAndSwap();
      }
    }

    public void ReloadSound() {
      if (!Active) {
        return;
      }

      var currentEngine = currentSwap_?.GetCurrentEngine();
      if (currentEngine != null && currentEngine.EngineId != 0) {
        SetSoundSound(core_.PlayerCar.Base, currentSound_);
      }
    }

    public void OnGui(Gui gui, ref float x, ref float y, float width) {
      if (!Active) {
        return;
      }

      const float listHeight = 220.0f;
      const float height = Gui.Height;

      bool allowSwap = core_.IsInGarage || core_.IsCheatsEnabled && core_.IsDevToolsEnabled;

      bool enabled = GUI.enabled;
      GUI.enabled = allowSwap;

      gui.BeginScrollV(ref x, ref y, width, listHeight, carListScrollH_, ref carListScroll_, Locale.Get("swaps_engines"));

      y += Gui.OffsetSmall;

      float sx = x;
      float sy = y;
      const float offset = Gui.ScrollBarWidth;
      bool scrollVisible = carListScrollH_ > listHeight;
      float w = scrollVisible ? width - (offset + Gui.OffsetSmall * 3.0f) : width - Gui.OffsetSmall * 3.0f;

      var currentEngine = currentSwap_?.GetCurrentEngine();
      int engineId = currentEngine?.EngineId ?? 0;
      if (gui.Button(ref sx, ref sy, w, height, "STOCK", engineId == 0 ? Skin.ButtonActive : Skin.Button)) {
        if (engineId != 0) {
          SetStockEngine(core_.PlayerCar.Base, -1, true);
          currentSwap_?.SetCurrentEngine(-1);
        }
      }

      bool carOk = !KnCar.IsNull(core_.PlayerCar);
      foreach (var engine in engines_) {
        bool allowed = carOk && balance_.Any(balance => balance.CarId == core_.PlayerCar.Id && balance.Rating >= engine.Rating);
        if (!core_.IsDevToolsEnabled && (!engine.Enabled || !allowed)) {
          continue;
        }

        if (gui.Button(ref sx, ref sy, w, height, engine.Name, engineId == engine.Id ? Skin.ButtonActive : Skin.Button)) {
          if (engineId != engine.Id) {
            SwapEngineTo(currentEngine, engine, engine.Id);
          }
        }
      }
      carListScrollH_ = gui.EndScrollV(ref x, ref y, sx, sy);

      // GUI.enabled = allowSwap && activeEngine_ != 0;
      // if (gui.SliderH(ref x, ref y, width, ref currentEngine_.Turbo, 0.0f, currentEngineTurboMax_, $"{Locale.Get("swaps_turbo")}: {currentEngine_.Turbo:F2}")) {
      //   var desc = core_.PlayerCar.Base.GetDesc();
      //   desc.carXDesc.engine.turboPressure = currentEngine_.Turbo;
      //   core_.PlayerCar.Base.SetDesc(desc);
      //
      //   foreach (var swap in allData_) {
      //     if (swap.CarId == currentEngine_.CarId && swap.EngineId == currentEngine_.EngineId) {
      //       swap.Turbo = currentEngine_.Turbo;
      //       break;
      //     }
      //   }
      // }
      // if (gui.SliderH(ref x, ref y, width, ref currentEngine_.FinalDrive, 2.5f, 5.0f, $"{Locale.Get("swaps_fd")}: {currentEngine_.FinalDrive:F2}")) {
      //   var desc = core_.PlayerCar.Base.GetDesc();
      //   desc.carXDesc.gearBox.finalDrive = currentEngine_.FinalDrive;
      //   core_.PlayerCar.Base.SetDesc(desc);
      //
      //   foreach (var swap in allData_) {
      //     if (swap.CarId == currentEngine_.CarId && swap.EngineId == currentEngine_.EngineId) {
      //       swap.FinalDrive = currentEngine_.FinalDrive;
      //       break;
      //     }
      //   }
      // }
      GUI.enabled = enabled;
    }

    public void OnUdpData(SmartfoxDataPackage data) {
      if (!dataLoaded_) {
        return;
      }

      int id = data.Data.GetInt("id");
      int engineId = data.Data.GetInt("ei");
      float turbo = data.Data.GetFloat("tb");
      float finalDrive = data.Data.GetFloat("fd");

      if (engineId == 0) {
        return;
      }

      if (core_.Settings.LogEngines) {
        Log.Write($"[KN_Core::Swaps]: Applying engine '{engineId}' on '{id}', turbo: {turbo}, finalDrive: {finalDrive}");
      }

    }

    private void SendSwapData() {

      // var nwData = new SmartfoxDataPackage(PacketId.Subroom);
      // nwData.Add("1", (byte) 25);
      // nwData.Add("type", Udp.TypeSwaps);
      // nwData.Add("id", id);
      // nwData.Add("ei", currentEngine_.EngineId);
      // nwData.Add("tb", currentEngine_.Turbo);
      // nwData.Add("fd", currentEngine_.FinalDrive);
      //
      // core_.Udp.Send(nwData);
    }

    private void FindAndSwap() {
      bool found = false;
      foreach (var swap in allData_) {
        if (swap.CarId == core_.PlayerCar.Id) {
          Log.Write($"[KN_Core::Swaps]: Found SwapData for car '{swap.CarId}'");

          var toSwap = swap.GetCurrentEngine();
          if (toSwap != null) {
            var engine = GetEngine(toSwap.EngineId);
            if (!SetEngine(core_.PlayerCar.Base, toSwap, engine, -1, true)) {
              swap.RemoveEngine(toSwap);
            }
          }

          currentSwap_ = swap;
          found = true;
          break;
        }
      }

      if (!found) {
        var swap = new SwapData(core_.PlayerCar.Id);
        allData_.Add(swap);
        currentSwap_ = swap;
        Log.Write($"[KN_Core::Swaps]: Created new SwapData for car '{swap.CarId}', swaps: {allData_.Count}");
      }
    }

    private bool SetEngine(RaceCar car, SwapData.Engine swap, EngineData engine, int nwId, bool self) {
      if (engine == null) {
        SetStockEngine(car, nwId, self);
        return false;
      }

      if (self && !Verify(car, swap, engine)) {
        SetStockEngine(car, nwId, true);
        Log.Write($"[KN_Core::Swaps]: Engine verification failed '{engine.Id}', applying default ({nwId})");
        return false;
      }

      var newEngine = new CarDesc.Engine();
      CopyEngine(engine.Engine, newEngine);

      newEngine.turboPressure = swap.Turbo;
      car.carX.finaldrive = swap.FinalDrive;
      car.carX.clutchMaxTorque = engine.ClutchTorque;

      var desc = car.GetDesc();
      CopyEngine(newEngine, desc.carXDesc.engine);
      car.SetDesc(desc);

      SetSoundSound(car, engine.SoundId);
      currentSound_ = engine.SoundId;

      Log.Write($"[KN_Core::Swaps]: Engine '{engine.Name}' ({engine.Id}) was set to '{car.metaInfo.id}' ({nwId}, self: {self})");

      return true;
    }

    private void SetSoundSound(RaceCar car, string soundId) {
      for (int i = 0; i < car.transform.childCount; ++i) {
        var child = car.transform.GetChild(i);
        if (child.name == "Engine") {
          var engineSound = child.GetComponent<FMODCarEngine>();
          if (engineSound != null) {
            var onEnable = KnUtils.GetMethod(engineSound, "OnEnableHandler");
            var onDisable = KnUtils.GetMethod(engineSound, "OnDisableHandler");

            var raceCar = KnUtils.GetField(engineSound, "m_raceCar") as RaceCar;
            if (raceCar != null) {
              onDisable?.Invoke(engineSound, new object[] { });

              string oldName = raceCar.metaInfo.name;
              raceCar.metaInfo.name = soundId;
              KnUtils.SetField(engineSound, "m_raceCar", raceCar);

              onEnable?.Invoke(engineSound, new object[] { });

              raceCar.metaInfo.name = oldName;
              KnUtils.SetField(engineSound, "m_raceCar", raceCar);

              Log.Write($"[KN_Core::Swaps]: Engine sound sound is set to '{car.metaInfo.id}'");
            }
          }
          break;
        }
      }
    }

    private bool Verify(RaceCar car, SwapData.Engine swap, EngineData engine) {
      bool allowed = balance_.Any(b => b.CarId == car.metaInfo.id && b.Rating >= engine.Rating);
      return swap.Turbo <= engine.Engine.turboPressure && allowed || core_.IsCheatsEnabled && core_.IsDevToolsEnabled;
    }

    private void SwapEngineTo(SwapData.Engine currentEngine, EngineData engine, int engineId) {
      if (currentEngine != null) {
        if (engineId > 0 && SetEngine(core_.PlayerCar.Base, currentEngine, engine, -1, true)) {
          if (!currentSwap_.SetCurrentEngine(engineId)) {
            AddNewEngineToCurrent(engine);
          }
        }
        else {
          currentSwap_.RemoveEngine(currentEngine);
        }
      }
      else if (currentSwap_ != null) {
        if (!currentSwap_.SetCurrentEngine(engineId)) {
          var newEngine = AddNewEngineToCurrent(engine);

          if (!SetEngine(core_.PlayerCar.Base, newEngine, engine, -1, true)) {
            currentSwap_.RemoveEngine(newEngine);
          }
        }
        else {
          var current = currentSwap_.GetCurrentEngine();
          SetEngine(core_.PlayerCar.Base, current, engine, -1, true);
        }
      }
    }

    private SwapData.Engine AddNewEngineToCurrent(EngineData engine) {
      var newEngine = new SwapData.Engine {
        EngineId = engine.Id,
        Turbo = engine.Engine.turboPressure,
        FinalDrive = core_.PlayerCar.CarX.finaldrive
      };
      currentSwap_.AddEngine(newEngine);

      return newEngine;
    }

    private void SetStockEngine(RaceCar car, int nwId, bool self) {
      var desc = car.GetDesc();
      CopyEngine(defaultEngine_, desc.carXDesc.engine);
      car.SetDesc(desc);

      car.carX.clutchMaxTorque = defaultClutch_;
      car.carX.finaldrive = defaultFinalDrive_;

      Log.Write($"[KN_Core::Swaps]: Stock engine was set to '{car.metaInfo.id}' ({nwId}, self: {self})");

      SetSoundSound(car, defaultSoundId_);
      currentSound_ = defaultSoundId_;
    }

    private EngineData GetEngine(int id) {
      if (id == 0) {
        return null;
      }

      foreach (var engine in engines_) {
        if (engine.Id == id) {
          return engine;
        }
      }

      Log.Write($"[KN_Core::Swaps]: Unable to find engine '{id}'");
      return null;
    }

    private void CopyEngine(CarDesc.Engine src, CarDesc.Engine dst) {
      dst.inertiaRatio = src.inertiaRatio;
      dst.maxTorque = src.maxTorque;
      dst.revLimiter = src.revLimiter;
      dst.turboCharged = src.turboCharged;
      dst.turboPressure = src.turboPressure;
      dst.brakeTorqueRatio = src.brakeTorqueRatio;
      dst.revLimiterStep = src.revLimiterStep;
      dst.useTC = src.useTC;
      dst.cutRPM = src.cutRPM;
      dst.idleRPM = src.idleRPM;
      dst.maxTorqueRPM = src.maxTorqueRPM;
    }
  }
}