# Notch Windows — Fase 1 & 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Membangun shell pill Windows (Core, PillWindow, IslandController, tray icon) beserta provider volume, brightness, media, dan baterai, sesuai Fase 1 dan 2 di spec.

**Architecture:** Alur satu arah Provider → EventBus → IslandController → PillWindow. `Core` murni logika tanpa dependensi Windows dan dites di semua OS. Provider hanya bicara lewat interface sumber (`IVolumeSource`, dst.), sehingga bisa dites dengan fake. Implementasi nyata interface itu ada di `Native`.

**Tech Stack:** C# 12, .NET 8 SDK, WPF + Windows Forms (tray), NAudio (volume), System.Management (WMI brightness), `Windows.Media.Control` (SMTC via WinRT), Serilog + Serilog.Sinks.File (log harian), xUnit (tes).

**Spec:** `docs/superpowers/specs/2026-10-25-notch-windows-design.md`

## Global Constraints

- Target framework: `net8.0` untuk `Core` dan `Core.Tests`. `net8.0-windows10.0.19041.0` untuk `App`, `Native`, dan `Providers`.
- Dukungan OS: Windows 10 22H2 dan Windows 11.
- Prioritas: Timer selesai 100, Notifikasi 80, Charging/baterai 60, Volume/brightness 40, Ganti lagu 20.
- Durasi tampil: Timer selesai sampai di-dismiss (sticky). Notifikasi 4 dtk. Charging/baterai 3 dtk. Volume/brightness 2 dtk. Ganti lagu 2,5 dtk.
- Antrean maksimal 5. Event yang tergantikan masuk antrean hanya jika sisa durasinya lebih dari 1 dtk.
- Animasi: expand 250 dtk ease-out. Konten fade-in setelah jeda 80 dtk. Collapse 200 dtk ease-in, konten fade-out dulu. Crossfade saat ganti event 120 dtk.
- Polling: brightness sekitar 1 Hz. Persentase baterai setiap 30 dtk.
- Penanganan error: provider dinonaktifkan untuk sesi setelah 3 kali gagal beruntun.
- Log di `%LOCALAPPDATA%\NotchWindows\logs`, dirotasi harian. Isi notifikasi tidak di-log.
- Posisi pill: top-center dari bounds monitor utama. Manifest `PerMonitorV2`.
- Pill tidak mengambil fokus (`WS_EX_NOACTIVATE`) dan tidak muncul di taskbar atau Alt-Tab (`WS_EX_TOOLWINDOW`).
- Idle: bar panjang tipis tanpa konten. Bisa dimatikan lewat pengaturan.
- Fullscreen: pill disembunyikan secara default.
- Target: event sampai pill terlihat ≤100 ms. CPU idle rata-rata di bawah 1%.

**Keputusan yang diambil di plan (tidak ada di spec):**
- Backoff restart provider: 5 dtk, tetap (bukan eksponensial).
- Ukuran idle bar: 180 × 28 DIP (default, bisa diubah di pengaturan).
- Pengaturan disimpan sebagai JSON di `%LOCALAPPDATA%\NotchWindows\settings.json`.
- Logging memakai Serilog karena rotasi harian sudah tersedia.
- Repo belum git. Langkah commit di setiap task hanya dijalankan setelah user menyetujui `git init`. Sampai saat itu, task dianggap selesai saat tesnya lulus.

## Review Focus

1. Volume 0% dan 100%, serta mute yang tidak mengubah level: tetap tampil, tanpa crash, nilai tetap dalam 0–100.
2. Media tanpa sesi SMTC (null) atau lagu tanpa judul/artis: ambient hilang tanpa exception, dan teks kosong tidak tampil sebagai "null".
3. Desktop tanpa baterai atau status baterai tidak diketahui: provider berstatus `Unavailable`, tanpa crash.
4. Badai event (slider volume digeser cepat, ratusan event per detik): antrean tetap dalam kapasitas, dan UI tidak terkunci.
5. Klik tombol media di pill yang expanded tidak mengambil fokus dari jendela aktif.

---

## File Structure

```
NotchWindows.sln
src/
  NotchWindows.Core/          IslandEvent, IslandKind, EventPriority, IClock, IslandQueue,
                              IslandController, EventBus, IEventProvider, ProviderHost,
                              AppSettings, AppSettingsStore, PillPlacement
  NotchWindows.Providers/     IVolumeSource, IBrightnessSource, IMediaSessionSource,
                              IPowerStatusSource, VolumeProvider, BrightnessProvider,
                              MediaProvider, BatteryProvider
  NotchWindows.Native/        User32 (WS_EX), NAudioVolumeSource, WmiBrightnessSource,
                              SmtcMediaSource, PowerStatusSource, FullscreenDetector
  NotchWindows.App/           App.xaml, Bootstrap (composition root), Shell/PillWindow,
                              Shell/PillView, ViewModels/IslandViewModel, Animation/PillAnimator,
                              Tray/TrayIcon, Views/ (per kind), Logging setup
tests/
  NotchWindows.Core.Tests/    Tes Core dan tes provider (dengan fake)
```

Dependensi antar-proyek: `Core` ← `Providers` ← `Native` ← `App`. `Providers` juga mereferensi `Core`. Tes provider ada di `Core.Tests` (dengan referensi ke `Providers`).

---

### Task 1: Scaffold solution dan tipe domain Core

**Files:**
- Create: `NotchWindows.sln`
- Create: `src/NotchWindows.Core/NotchWindows.Core.csproj`
- Create: `src/NotchWindows.Core/IslandKind.cs`
- Create: `src/NotchWindows.Core/EventPriority.cs`
- Create: `src/NotchWindows.Core/IslandEvent.cs`
- Create: `src/NotchWindows.Core/IClock.cs`, `src/NotchWindows.Core/SystemClock.cs`
- Create: `tests/NotchWindows.Core.Tests/NotchWindows.Core.Tests.csproj`
- Create: `tests/NotchWindows.Core.Tests/ManualClock.cs`
- Create: `tests/NotchWindows.Core.Tests/EventPriorityTests.cs`

**Interfaces:**
- Produces:
  - `enum IslandKind { Timer, Notification, Battery, Volume, Brightness, Media }`
  - `static class EventPriority` dengan `const int`: `Timer = 100`, `Notification = 80`, `Battery = 60`, `Volume = 40`, `Brightness = 40`, `Media = 20`
  - `sealed record IslandEvent(IslandKind Kind, int Priority, TimeSpan? Duration, string? Key = null, object? Payload = null)`. `Duration == null` berarti sticky.
  - `interface IClock { DateTimeOffset Now { get; } }`, dengan `SystemClock` sebagai implementasi nyata.
  - `ManualClock` di tes: `Now` yang bisa diset, dan `Advance(TimeSpan)`.

- [ ] **Step 0: Pasang .NET 8 SDK**

Run: `winget install Microsoft.DotNet.SDK.8`
Expected: setelah instalasi, `dotnet --list-sdks` menampilkan baris `8.0.x`. Saat ini hanya runtime yang terpasang, jadi langkah ini wajib.

- [ ] **Step 1: Tulis tes urutan prioritas (gagal dulu)**

```csharp
[Fact]
public void Timer_IsHighest_And_Media_IsLowest()
{
    Assert.True(EventPriority.Timer > EventPriority.Notification);
    Assert.True(EventPriority.Notification > EventPriority.Battery);
    Assert.True(EventPriority.Battery > EventPriority.Volume);
    Assert.Equal(EventPriority.Volume, EventPriority.Brightness);
    Assert.True(EventPriority.Volume > EventPriority.Media);
}
```

- [ ] **Step 2: Jalankan dan pastikan gagal**

Run: `dotnet test tests/NotchWindows.Core.Tests`
Expected: FAIL, karena `EventPriority` belum ada (error kompilasi).

- [ ] **Step 3: Implementasi tipe domain**

Buat `IslandKind`, `EventPriority` (nilai sesuai Interfaces), `IslandEvent` sebagai record, serta `IClock` dan `SystemClock` (`DateTimeOffset.Now`). `Core.csproj` memakai `net8.0` dengan `Nullable` aktif. Tes memakai xUnit.

- [ ] **Step 4: Jalankan dan pastikan lulus**

Run: `dotnet test tests/NotchWindows.Core.Tests`
Expected: PASS, 1 test.

- [ ] **Step 5: Commit (opsional, hanya jika repo git sudah ada)**

---

### Task 2: IslandQueue (antrean, preemption, coalescing, kapasitas)

**Files:**
- Create: `src/NotchWindows.Core/IslandQueue.cs`
- Create: `tests/NotchWindows.Core.Tests/IslandQueueTests.cs`

**Interfaces:**
- Consumes: `IClock`, `IslandEvent`, `EventPriority`
- Produces:
  - `sealed class IslandQueue(IClock clock, int capacity = 5)`
  - `IslandEvent? Current { get; }`
  - `DateTimeOffset? ExpiresAt { get; }`, `null` jika sticky atau tidak ada event
  - `int Count { get; }`, jumlah entri di antrean (tidak termasuk Current)
  - `void Enqueue(IslandEvent e)`
  - `void Tick()`, mempromosikan event berikutnya jika Current sudah habis
  - `void Dismiss()`, menghapus Current (dipakai untuk timer sticky)
  - `void SetPaused(bool paused)`, saat paused, `ExpiresAt` diperpanjang sebesar durasi jeda

**Aturan** (sesuai spec bagian 5):
- Current kosong → event langsung menjadi Current.
- Prioritas lebih tinggi dari Current → preempt. Current lama masuk antrean hanya jika sisa durasinya lebih dari 1 dtk, selain itu dibuang. Current sticky tidak pernah dibuang.
- `Key` sama dengan Current → menggantikan Current dan mereset `ExpiresAt`.
- `Key` sama dengan entri antrean → menggantikan entri itu.
- Kapasitas penuh → buang entri prioritas terendah. Jika seri, buang yang paling lama.
- `Tick` setelah Current habis → promosikan entri prioritas tertinggi (seri: FIFO).

- [ ] **Step 1: Tulis tes (gagal dulu)**

Nama tes yang harus ada:
- `Enqueue_WhenEmpty_BecomesCurrent`
- `Enqueue_HigherPriority_PreemptsCurrent`
- `Enqueue_Preempted_ReQueuedWhenMoreThanOneSecondRemains`
- `Enqueue_Preempted_DroppedWhenOneSecondOrLessRemains`
- `Enqueue_SameKey_ReplacesCurrentAndResetsExpiry`
- `Enqueue_SameKeyInQueue_ReplacesEntry`
- `Enqueue_OverCapacity_DropsLowestPriority`
- `Tick_AfterDuration_PromotesHighestPriority`
- `Tick_StickyEvent_NeverExpires`
- `Dismiss_RemovesStickyCurrent`
- `SetPaused_ExtendsExpiryByPausedDuration`
- `Storm_HundredsOfVolumeEvents_KeepsCountWithinCapacity` (review focus #4)

Contoh dua tes kunci:

```csharp
[Fact]
public void Enqueue_Preempted_DroppedWhenOneSecondOrLessRemains()
{
    var clock = new ManualClock();
    var q = new IslandQueue(clock);
    q.Enqueue(new IslandEvent(IslandKind.Volume, EventPriority.Volume, TimeSpan.FromSeconds(2), "volume"));
    clock.Advance(TimeSpan.FromMilliseconds(1500)); // sisa 0,5 dtk
    q.Enqueue(new IslandEvent(IslandKind.Notification, EventPriority.Notification, TimeSpan.FromSeconds(4)));
    Assert.Equal(IslandKind.Notification, q.Current!.Kind);
    Assert.Equal(0, q.Count); // volume dibuang
}

[Fact]
public void Storm_HundredsOfVolumeEvents_KeepsCountWithinCapacity()
{
    var q = new IslandQueue(new ManualClock());
    for (var i = 0; i < 500; i++)
        q.Enqueue(new IslandEvent(IslandKind.Brightness, EventPriority.Brightness, TimeSpan.FromSeconds(2), $"k{i}"));
    Assert.True(q.Count <= 4); // kapasitas 5 termasuk Current
}
```

- [ ] **Step 2: Jalankan dan pastikan gagal**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter IslandQueueTests`
Expected: FAIL, karena `IslandQueue` belum ada.

- [ ] **Step 3: Implementasi `IslandQueue`**

Simpan Current dan `ExpiresAt`, serta daftar antrean. Catat waktu jeda saat `SetPaused(true)` lalu geser `ExpiresAt` saat `SetPaused(false)`. Kapasitas dihitung termasuk Current.

- [ ] **Step 4: Jalankan dan pastikan lulus**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter IslandQueueTests`
Expected: PASS, semua tes di atas.

- [ ] **Step 5: Commit (opsional)**

---

### Task 3: IslandController (state dan ambient)

**Files:**
- Create: `src/NotchWindows.Core/IslandState.cs`
- Create: `src/NotchWindows.Core/AmbientUpdate.cs`
- Create: `src/NotchWindows.Core/IslandController.cs`
- Create: `tests/NotchWindows.Core.Tests/IslandControllerTests.cs`

**Interfaces:**
- Consumes: `IslandQueue`, `IClock`, `IslandEvent`
- Produces:
  - `enum IslandState { Idle, Compact, Expanded }`
  - `sealed record AmbientUpdate(IslandKind Kind, object? Payload)`, dengan `Payload == null` berarti hapus ambient untuk Kind itu
  - `sealed class IslandController(IslandQueue queue, IClock clock)`
    - `IslandState State { get; }`
    - `IslandEvent? Current { get; }`
    - `IReadOnlyDictionary<IslandKind, object?> Ambient { get; }`
    - `event EventHandler<IslandState>? StateChanged`, hanya saat state benar-benar berubah
    - `void Publish(IslandEvent e)`
    - `void ApplyAmbient(AmbientUpdate u)`
    - `void Tick()`
    - `void SetPointerOver(bool over)`, meneruskan ke `queue.SetPaused`
    - `void Dismiss()`

**Aturan state:** jika ada Current → `Expanded`. Jika tidak ada Current tetapi ada ambient → `Compact`. Jika keduanya kosong → `Idle`.

- [ ] **Step 1: Tulis tes (gagal dulu)**

- `State_NoEventNoAmbient_IsIdle`
- `State_AmbientOnly_IsCompact`
- `State_EventWhileAmbient_IsExpanded`
- `StateChanged_RaisedOnlyWhenStateChanges`
- `ApplyAmbient_NullPayload_ClearsKind`
- `SetPointerOver_True_PausesAutoCollapse`

- [ ] **Step 2: Jalankan dan pastikan gagal**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter IslandControllerTests`
Expected: FAIL.

- [ ] **Step 3: Implementasi**

Hitung ulang state setiap kali Current atau ambient berubah. Bandingkan state lama dan baru sebelum memanggil `StateChanged`.

- [ ] **Step 4: Jalankan dan pastikan lulus**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter IslandControllerTests`
Expected: PASS.

- [ ] **Step 5: Commit (opsional)**

---

### Task 4: EventBus dan ProviderHost (dengan penanganan error)

**Files:**
- Create: `src/NotchWindows.Core/IEventProvider.cs` (termasuk `enum ProviderStatus`)
- Create: `src/NotchWindows.Core/EventBus.cs`
- Create: `src/NotchWindows.Core/ProviderHost.cs`
- Create: `tests/NotchWindows.Core.Tests/EventBusTests.cs`
- Create: `tests/NotchWindows.Core.Tests/ProviderHostTests.cs`
- Modify: `src/NotchWindows.Core/NotchWindows.Core.csproj` (tambahkan `Microsoft.Extensions.Logging.Abstractions`)

**Interfaces:**
- Consumes: `IslandEvent`, `AmbientUpdate`
- Produces:
  - `enum ProviderStatus { Stopped, Running, Unavailable, Disabled }`
  - `interface IEventProvider`
    - `string Name { get; }`
    - `ProviderStatus Status { get; }`
    - `event Action<IslandEvent>? Raised`
    - `event Action<AmbientUpdate>? AmbientChanged`
    - `event Action<Exception>? Faulted`
    - `void Start()`, `void Stop()`
  - `sealed class EventBus(Action<Action>? dispatch = null)`
    - `void Subscribe(Action<IslandEvent> handler)`
    - `void SubscribeAmbient(Action<AmbientUpdate> handler)`
    - `void Publish(IslandEvent e)`, `void PublishAmbient(AmbientUpdate u)`, keduanya melewati `dispatch` jika ada, dan default-nya langsung dipanggil
  - `sealed class ProviderHost(EventBus bus, IEnumerable<IEventProvider> providers, TimeSpan retryDelay, ILogger<ProviderHost> logger)`
    - `void StartAll()`

**Aturan ProviderHost:**
- Setiap `Raised` dan `AmbientChanged` diteruskan ke bus, dan menghapus penghitung gagal provider itu.
- `Faulted`, atau `Start()` yang melempar exception, menaikkan penghitung gagal. Setelah `retryDelay`, provider di-`Stop()` lalu di-`Start()` ulang.
- Penghitung mencapai 3 → status `Disabled`, dan provider tidak di-restart lagi untuk sesi ini.
- Provider berstatus `Unavailable` (diatur sendiri oleh provider) tidak di-restart.
- Kegagalan satu provider tidak memengaruhi provider lain.

- [ ] **Step 1: Tulis tes (gagal dulu)**

EventBus:
- `Publish_DeliversToAllSubscribers`
- `Publish_UsesDispatcher`, memastikan dispatcher dipanggil dengan callback yang benar
- `PublishAmbient_DeliversToAmbientSubscribers`

ProviderHost (memakai fake `IEventProvider` dan `retryDelay = TimeSpan.Zero`, lalu menunggu kondisi dengan timeout 1 dtk):
- `Host_RestartsFaultedProvider`
- `Host_DisablesProviderAfterThreeConsecutiveFailures`
- `Host_FailureCounterResetsWhenProviderRaisesEvent`
- `Host_ProviderStartThrows_CountsAsFailure`
- `Host_FaultOnOneProvider_DoesNotAffectOthers`
- `Host_UnavailableProvider_IsNotRestarted`

- [ ] **Step 2: Jalankan dan pastikan gagal**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter "EventBusTests|ProviderHostTests"`
Expected: FAIL.

- [ ] **Step 3: Implementasi**

Pakai `ILogger` untuk mencatat restart dan penonaktifan. Jangan log isi event, supaya teks notifikasi tidak tercatat.

- [ ] **Step 4: Jalankan dan pastikan lulus**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter "EventBusTests|ProviderHostTests"`
Expected: PASS.

- [ ] **Step 5: Commit (opsional)**

---

### Task 5: Logging dan penanganan exception global (App)

**Files:**
- Create: `src/NotchWindows.App/NotchWindows.App.csproj` (WPF, `UseWindowsForms`, `net8.0-windows10.0.19041.0`, referensi ke `Core`)
- Create: `src/NotchWindows.App/Logging.cs`
- Create: `src/NotchWindows.App/App.xaml`, `src/NotchWindows.App/App.xaml.cs`
- Create: `src/NotchWindows.App/app.manifest` (`PerMonitorV2`)
- Create: `NotchWindows.sln` entry untuk App

**Interfaces:**
- Produces:
  - `static class Logging { static ILoggerFactory Create(); }`, Serilog dengan sink file `%LOCALAPPDATA%\NotchWindows\logs\notch-.log`, `RollingInterval.Day`
  - `App.OnStartup` memasang `DispatcherUnhandledException` dan `AppDomain.CurrentDomain.UnhandledException` untuk mencatat exception dan menjaga shell tetap hidup (`e.Handled = true` untuk dispatcher)

- [ ] **Step 1: Buat project App dan jalankan kosong**

Run: `dotnet run --project src/NotchWindows.App`
Expected: aplikasi kosong berjalan tanpa error build.

- [ ] **Step 2: Verifikasi log terbentuk**

Lempar exception sementara di `OnStartup`, lalu jalankan.
Expected: file `%LOCALAPPDATA%\NotchWindows\logs\notch-YYYYMMDD.log` berisi stack trace, dan aplikasi tidak langsung mati. Hapus exception sementara setelah verifikasi.

- [ ] **Step 3: Verifikasi manifest**

Run: `dotnet build src/NotchWindows.App`
Expected: build sukses, dan `app.manifest` terpasang di output.

- [ ] **Step 4: Commit (opsional)**

---

### Task 6: AppSettings dan PillPlacement (Core, TDD)

**Files:**
- Create: `src/NotchWindows.Core/AppSettings.cs`
- Create: `src/NotchWindows.Core/AppSettingsStore.cs`
- Create: `src/NotchWindows.Core/PillPlacement.cs`
- Create: `tests/NotchWindows.Core.Tests/AppSettingsStoreTests.cs`
- Create: `tests/NotchWindows.Core.Tests/PillPlacementTests.cs`

**Interfaces:**
- Produces:
  - `sealed record AppSettings(bool IdleBarEnabled = true, double IdleBarWidth = 180, double IdleBarHeight = 28, bool HideInFullscreen = true)`
  - `static class AppSettingsStore`, `static AppSettings Load(string path)` dan `static void Save(string path, AppSettings settings)`
  - `static class PillPlacement`, `static double CenterLeft(double screenLeft, double screenWidth, double pillWidth)`

**Aturan:** `Load` mengembalikan default jika file tidak ada atau JSON rusak (dan mencatat tanpa melempar). `Save` membuat folder jika belum ada.

- [ ] **Step 1: Tulis tes (gagal dulu)**

- `Load_MissingFile_ReturnsDefaults`
- `Load_CorruptJson_ReturnsDefaults`
- `Save_ThenLoad_RoundTrips`
- `CenterLeft_CentersPillOnScreen`, misalnya `CenterLeft(0, 1920, 180) == 870`
- `CenterLeft_NonZeroScreenLeft_OffsetsCorrectly`

- [ ] **Step 2: Jalankan dan pastikan gagal**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter "AppSettingsStoreTests|PillPlacementTests"`
Expected: FAIL.

- [ ] **Step 3: Implementasi**

Pakai `System.Text.Json`. Nilai default sesuai Global Constraints.

- [ ] **Step 4: Jalankan dan pastikan lulus**

Run: `dotnet test tests/NotchWindows.Core.Tests`
Expected: semua tes Core lulus.

- [ ] **Step 5: Commit (opsional)**

---

### Task 7: PillWindow (shell WPF, non-activating)

**Files:**
- Create: `src/NotchWindows.Native/NotchWindows.Native.csproj` (`net8.0-windows10.0.19041.0`)
- Create: `src/NotchWindows.Native/User32.cs`
- Create: `src/NotchWindows.App/Shell/PillWindow.xaml`, `src/NotchWindows.App/Shell/PillWindow.xaml.cs`

**Interfaces:**
- Produces:
  - `static class User32`, `static void MakeNonActivatingToolWindow(IntPtr hwnd)`, yang menambahkan `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` lewat `SetWindowLongPtr` / `GetWindowLongPtr`
  - `PillWindow : Window`, dengan `void PlaceTopCenter()` yang menghitung posisi dengan `PillPlacement.CenterLeft` dan mengatur `Top = 0` pada `SystemParameters.PrimaryScreenWidth` (DIP)
  - `PillWindow` menyetel `WindowStyle="None"`, `AllowsTransparency="True"`, `Topmost="True"`, `ShowInTaskbar="False"`, `ResizeMode="NoResize"`, `Background="Transparent"`

**Aturan:** `PillPlacement` dipanggil lewat `PlaceTopCenter()`. Panggil `MakeNonActivatingToolWindow` di `SourceInitialized`. Panggil ulang `PlaceTopCenter()` saat `SystemEvents.DisplaySettingsChanged` dan `DpiChanged`.

- [ ] **Step 1: Build**

Run: `dotnet build src/NotchWindows.App`
Expected: build sukses.

- [ ] **Step 2: Verifikasi manual posisi dan fokus**

Run: `dotnet run --project src/NotchWindows.App`
Expected:
- Bar kosong (placeholder) tampil di tengah atas monitor utama, dan tidak ada di taskbar atau Alt-Tab.
- Buka Notepad, mulai mengetik, lalu klik bar kosong. Expected: fokus tetap di Notepad dan ketikan tidak berpindah.

- [ ] **Step 3: Catat hasil**

Jika ada langkah yang gagal, tulis di laporan, jangan lanjut ke task berikutnya tanpa memperbaikinya.

- [ ] **Step 4: Commit (opsional)**

---

### Task 8: IslandViewModel dan PillAnimator

**Files:**
- Create: `src/NotchWindows.App/ViewModels/IslandViewModel.cs`
- Create: `src/NotchWindows.App/Animation/PillAnimator.cs`
- Create: `src/NotchWindows.App/Shell/PillView.xaml`, `src/NotchWindows.App/Shell/PillView.xaml.cs`
- Modify: `src/NotchWindows.App/Shell/PillWindow.xaml` (tampilkan `PillView` sebagai konten)

**Interfaces:**
- Consumes: `IslandController`, `IslandState`, `IslandEvent`
- Produces:
  - `IslandViewModel(IslandController controller)`, dengan properti `State`, `Current`, dan `Ambient`, serta `INotifyPropertyChanged`
  - `PillAnimator`, `void Animate(IslandState from, IslandState to)`, memakai durasi dan easing dari Global Constraints

- [ ] **Step 1: Implementasi ViewModel**

Berlangganan `StateChanged` dari controller dan memberi tahu UI saat properti berubah.

- [ ] **Step 2: Implementasi animasi**

Satu `Border` untuk lebar dan tinggi, dengan `Storyboard`. Expand 250 dtk ease-out. Konten fade-in dengan `BeginTime` 80 dtk. Collapse 200 dtk ease-in, konten fade-out dulu. Crossfade 120 dtk saat ganti event tanpa collapse.

- [ ] **Step 3: Verifikasi manual (tidak ada unit test untuk animasi)**

Run: `dotnet run --project src/NotchWindows.App`
Expected: lewat tombol debug sementara (dihapus setelah verifikasi), pill bergantian antara Idle, Compact, dan Expanded. Durasi terasa sesuai, tidak ada loncatan, dan teks tidak terpotong saat expand.

- [ ] **Step 4: Commit (opsional)**

---

### Task 9: Idle bar dan tray icon

**Files:**
- Create: `src/NotchWindows.App/Tray/TrayIcon.cs`
- Modify: `src/NotchWindows.App/Shell/PillView.xaml` (tampilkan bar tipis saat Idle dan `IdleBarEnabled`)

**Interfaces:**
- Consumes: `AppSettings`, `AppSettingsStore`, `IslandController`
- Produces:
  - `TrayIcon` dengan menu: "Idle bar: Aktif/Nonaktif" (toggle, disimpan ke settings), dan "Keluar"

**Aturan:** saat Idle dan `IdleBarEnabled == true`, tampilkan bar 180 × 28 DIP tanpa konten. Saat `IdleBarEnabled == false`, pill tidak tampil saat Idle.

- [ ] **Step 1: Implementasi tray**

`NotifyIcon` dari Windows Forms. Toggle idle bar memanggil `AppSettingsStore.Save` dan memperbarui tampilan.

- [ ] **Step 2: Verifikasi manual**

Run: `dotnet run --project src/NotchWindows.App`
Expected:
- Ikon tray muncul. Menu "Idle bar" mematikan dan menghidupkan bar tipis. Pengaturan bertahan setelah aplikasi dibuka ulang.
- "Keluar" menutup aplikasi dengan bersih.

- [ ] **Step 3: Commit (opsional)**

---

### Task 10: Deteksi fullscreen

**Files:**
- Create: `src/NotchWindows.Native/FullscreenDetector.cs`
- Modify: `src/NotchWindows.App/Shell/PillWindow.xaml.cs` (sembunyikan window saat fullscreen dan `HideInFullscreen == true`)

**Interfaces:**
- Produces:
  - `static class FullscreenDetector`, `static bool IsBusy()`, memanggil `SHQueryUserNotificationState` dan mengembalikan `true` untuk `QUNS_BUSY`, `QUNS_RUNNING_D3D_FULL_SCREEN`, dan `QUNS_PRESENTATION_MODE`

**Aturan:** dicek ulang lewat timer ringan (sekitar 1 dtk). Saat busy dan `HideInFullscreen`, `PillWindow` disembunyikan. Saat selesai, ditampilkan lagi.

- [ ] **Step 1: Verifikasi manual**

Run: `dotnet run --project src/NotchWindows.App`
Expected: buka video atau game dalam mode fullscreen. Pill hilang dalam sekitar 1 dtk. Keluar dari fullscreen, pill muncul lagi.

- [ ] **Step 2: Commit (opsional)**

---

### Task 11: VolumeProvider

**Files:**
- Create: `src/NotchWindows.Providers/NotchWindows.Providers.csproj` (`net8.0-windows10.0.19041.0`, referensi ke `Core`)
- Create: `src/NotchWindows.Providers/IVolumeSource.cs`
- Create: `src/NotchWindows.Providers/VolumeProvider.cs`
- Create: `src/NotchWindows.Native/NAudioVolumeSource.cs`
- Create: `tests/NotchWindows.Core.Tests/VolumeProviderTests.cs` (referensi ke `Providers`)

**Interfaces:**
- Produces:
  - `interface IVolumeSource { float Level { get; } bool IsMuted { get; } event Action? Changed; }`, `Level` dalam rentang 0.0–1.0
  - `sealed class VolumeProvider(IVolumeSource source, IClock clock) : IEventProvider`, `Name = "Volume"`
  - `sealed record VolumeInfo(int Percent, bool IsMuted)`, dipakai sebagai `Payload`
- Consumes: `IVolumeSource`, `IClock`, `IslandEvent`

**Aturan:** setiap `Changed` menghasilkan `IslandEvent(IslandKind.Volume, EventPriority.Volume, TimeSpan.FromSeconds(2), "volume", new VolumeInfo(...))`. `Percent` = `Math.Round(Level * 100)` dan dibatasi 0–100. Mute tetap mengirim level dengan `IsMuted = true`.

- [ ] **Step 1: Tulis tes (gagal dulu)**

- `Level_Zero_ReportsZeroPercent`
- `Level_One_ReportsHundredPercent`
- `Mute_KeepsLevelAndReportsMuted` (review focus #1)
- `Level_OutOfRange_IsClamped` (review focus #1)
- `Changed_RaisesVolumeEventWithSameKey`

Tes memakai fake `IVolumeSource` yang memicu `Changed` secara manual.

- [ ] **Step 2: Jalankan dan pastikan gagal**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter VolumeProviderTests`
Expected: FAIL.

- [ ] **Step 3: Implementasi**

`NAudioVolumeSource` memakai `MMDeviceEnumerator` untuk endpoint render default, dan `AudioEndpointVolume.OnVolumeNotification` untuk `Changed`. Provider hanya memakai `IVolumeSource`.

- [ ] **Step 4: Jalankan dan pastikan lulus**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter VolumeProviderTests`
Expected: PASS.

- [ ] **Step 5: Verifikasi manual dan commit (opsional)**

Run: `dotnet run --project src/NotchWindows.App`, lalu geser slider volume. Expected: pill expand dengan nilai volume, dan event berganti (bukan menumpuk) saat slider digeser cepat.

---

### Task 12: BrightnessProvider

**Files:**
- Create: `src/NotchWindows.Providers/IBrightnessSource.cs`
- Create: `src/NotchWindows.Providers/BrightnessProvider.cs`
- Create: `src/NotchWindows.Native/WmiBrightnessSource.cs`
- Create: `tests/NotchWindows.Core.Tests/BrightnessProviderTests.cs`

**Interfaces:**
- Produces:
  - `interface IBrightnessSource { int? Read(); }`, `null` berarti tidak didukung
  - `sealed class BrightnessProvider(IBrightnessSource source, IClock clock) : IEventProvider`, `Name = "Brightness"`, dengan `public void Poll()` yang dipanggil timer 1 Hz saat `Start()`
- Consumes: `IBrightnessSource`, `IClock`

**Aturan:** `Poll()` pertama kali membaca nilai. Jika `null`, status menjadi `Unavailable` dan polling berhenti. Event hanya dikirim saat nilai berubah, dengan `Key = "brightness"` dan durasi 2 dtk.

- [ ] **Step 1: Tulis tes (gagal dulu)**

- `Poll_Unsupported_SetsUnavailable`
- `Poll_UnchangedValue_RaisesNothing`
- `Poll_ChangedValue_RaisesBrightnessEvent`
- `Poll_NotifiesOnceWhileValueKeepsChanging`, memastikan event tidak dikirim saat nilai sama

- [ ] **Step 2: Jalankan dan pastikan gagal**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter BrightnessProviderTests`
Expected: FAIL.

- [ ] **Step 3: Implementasi**

`WmiBrightnessSource` membaca `WmiMonitorBrightness` di namespace `root\WMI`. Jika koleksi kosong (desktop), kembalikan `null`.

- [ ] **Step 4: Jalankan dan pastikan lulus, lalu verifikasi manual**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter BrightnessProviderTests`
Expected: PASS. Di laptop, ubah brightness dan pastikan pill muncul. Di desktop, provider harus berstatus `Unavailable` tanpa error.

- [ ] **Step 5: Commit (opsional)**

---

### Task 13: MediaProvider dan kontrol media

**Files:**
- Create: `src/NotchWindows.Providers/IMediaSessionSource.cs`
- Create: `src/NotchWindows.Providers/MediaProvider.cs`
- Create: `src/NotchWindows.Native/SmtcMediaSource.cs`
- Create: `src/NotchWindows.App/Views/MediaView.xaml`, `src/NotchWindows.App/Views/MediaView.xaml.cs`
- Create: `tests/NotchWindows.Core.Tests/MediaProviderTests.cs`

**Interfaces:**
- Produces:
  - `sealed record MediaSnapshot(string Title, string Artist, bool IsPlaying, byte[]? Thumbnail)`
  - `interface IMediaSessionSource { MediaSnapshot? Current { get; } event Action? Changed; Task TogglePlayPauseAsync(); Task SkipNextAsync(); }`
  - `sealed class MediaProvider(IMediaSessionSource source) : IEventProvider`, `Name = "Media"`
- Consumes: `IMediaSessionSource`, `IslandEvent`, `AmbientUpdate`

**Aturan:**
- Jika `Current` berubah judul (`Title`), kirim `IslandEvent(IslandKind.Media, EventPriority.Media, TimeSpan.FromMilliseconds(2500), "media-track", snapshot)`.
- Jika `IsPlaying == true`, kirim `AmbientUpdate(IslandKind.Media, snapshot)`. Jika `Current == null` atau paused, kirim `AmbientUpdate(IslandKind.Media, null)`.
- Teks kosong diganti dengan string kosong yang aman, bukan `"null"`.

- [ ] **Step 1: Tulis tes (gagal dulu)**

- `NoSession_ClearsAmbient` (review focus #2)
- `Playing_SetsAmbientWithSnapshot`
- `Paused_ClearsAmbient`
- `TrackChange_RaisesMediaEvent`
- `SameTrack_NoTransientEvent`
- `NullTitleAndArtist_UseEmptyStrings` (review focus #2)

- [ ] **Step 2: Jalankan dan pastikan gagal**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter MediaProviderTests`
Expected: FAIL.

- [ ] **Step 3: Implementasi**

`SmtcMediaSource` memakai `GlobalSystemMediaTransportControlsSessionManager`, mendengarkan `CurrentSessionChanged`, `PlaybackInfoChanged`, dan `MediaPropertiesChanged`. Kontrol memakai `TryTogglePlayPauseAsync` dan `TrySkipNextAsync`.

- [ ] **Step 4: Jalankan dan pastikan lulus**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter MediaProviderTests`
Expected: PASS.

- [ ] **Step 5: Verifikasi manual**

Run: `dotnet run --project src/NotchWindows.App`, lalu putar musik di Spotify atau browser.
Expected: ikon media muncul di compact saat main, dan pill expand sebentar saat lagu ganti. Di pill yang expanded, tombol play/pause/next berfungsi, dan fokus tetap di aplikasi yang sedang dipakai (review focus #5). Tutup semua pemutar, dan ambient hilang.

- [ ] **Step 6: Commit (opsional)**

---

### Task 14: BatteryProvider

**Files:**
- Create: `src/NotchWindows.Providers/IPowerStatusSource.cs`
- Create: `src/NotchWindows.Providers/BatteryProvider.cs`
- Create: `src/NotchWindows.Native/PowerStatusSource.cs`
- Create: `src/NotchWindows.App/Views/BatteryView.xaml`, `src/NotchWindows.App/Views/BatteryView.xaml.cs`
- Create: `tests/NotchWindows.Core.Tests/BatteryProviderTests.cs`

**Interfaces:**
- Produces:
  - `sealed record PowerSnapshot(bool HasBattery, int? Percent, bool IsCharging)`
  - `interface IPowerStatusSource { PowerSnapshot Read(); event Action? Changed; }`
  - `sealed class BatteryProvider(IPowerStatusSource source, IClock clock) : IEventProvider`, `Name = "Battery"`, dengan `public void Poll()` yang dipanggil timer 30 dtk
- Consumes: `IPowerStatusSource`, `IClock`

**Aturan:**
- `HasBattery == false` pada pembacaan pertama → status `Unavailable`, tanpa event.
- Saat `IsCharging` berubah (colok atau cabut), kirim `IslandEvent(IslandKind.Battery, EventPriority.Battery, TimeSpan.FromSeconds(3), "battery", snapshot)`.
- `Changed` dari source memicu pembacaan ulang, tanpa menunggu poll.

- [ ] **Step 1: Tulis tes (gagal dulu)**

- `NoBattery_SetsUnavailable` (review focus #3)
- `UnknownPercent_StillRaisesChargingEvent` (review focus #3)
- `PlugIn_RaisesChargingEvent`
- `NoChange_RaisesNothing`
- `Poll_DoesNotRaiseWhenPercentChangesOnly`

- [ ] **Step 2: Jalankan dan pastikan gagal**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter BatteryProviderTests`
Expected: FAIL.

- [ ] **Step 3: Implementasi**

`PowerStatusSource` memanggil `GetSystemPowerStatus` lewat P/Invoke. `BatteryFlag` 128 berarti tidak ada baterai. `BatteryLifePercent` 255 berarti tidak diketahui, dan dipetakan ke `Percent = null`.

- [ ] **Step 4: Jalankan dan pastikan lulus, lalu verifikasi manual**

Run: `dotnet test tests/NotchWindows.Core.Tests --filter BatteryProviderTests`
Expected: PASS. Di laptop, cabut dan colok charger, lalu pastikan event muncul. Di desktop, provider harus `Unavailable` tanpa error.

- [ ] **Step 5: Commit (opsional)**

---

### Task 15: Composition root, view per kind, dan overlay latensi debug

**Files:**
- Create: `src/NotchWindows.App/Bootstrap.cs`
- Create: `src/NotchWindows.App/Views/VolumeView.xaml`, `BrightnessView.xaml`, dan code-behind-nya
- Modify: `src/NotchWindows.App/App.xaml.cs` (panggil `Bootstrap`)

**Interfaces:**
- Consumes: semua provider, `EventBus`, `ProviderHost`, `IslandController`, `AppSettingsStore`
- Produces:
  - `Bootstrap.Start(Application app)`, yang membangun semua komponen, mendaftarkan provider hanya jika hardware mendukung (`IBrightnessSource.Read() != null`, `PowerSnapshot.HasBattery`), lalu memanggil `ProviderHost.StartAll()`
  - Dispatcher di `EventBus` diisi `Application.Current.Dispatcher.Invoke`

**Aturan:**
- Provider yang tidak didukung hardware tidak didaftarkan sama sekali.
- Overlay latensi (hanya build Debug): tampilkan selisih `Stopwatch` antara waktu event dibuat dan waktu pill mulai expand. Verifikasi target ≤100 ms.

- [ ] **Step 1: Implementasi composition root**

Pasang semua komponen dan wiring: `Provider → EventBus → IslandController → IslandViewModel → PillView`.

- [ ] **Step 2: Verifikasi end-to-end**

Run: `dotnet run --project src/NotchWindows.App`
Expected: volume, brightness (laptop), media, dan baterai bekerja bersamaan. Antrean tidak melebihi kapasitas saat slider digeser cepat (review focus #4).

- [ ] **Step 3: Ukur latensi**

Expected: overlay debug menunjukkan latensi ≤100 ms untuk volume dan media pada beberapa percobaan.

- [ ] **Step 4: Commit (opsional)**

---

### Task 16: Verifikasi akhir Fase 1 & 2

- [ ] **Step 1: Jalankan semua tes**

Run: `dotnet test NotchWindows.sln`
Expected: semua tes Core dan provider lulus.

- [ ] **Step 2: Checklist manual dari spec bagian 12**

- Windows 10 22H2 dan Windows 11
- DPI 100%, 125%, 150%, dan 200%
- Multi-monitor: pill tetap di monitor utama
- Tema terang dan gelap

- [ ] **Step 3: Ukur CPU idle**

Run: biarkan aplikasi berjalan 5 menit tanpa interaksi, lalu cek di Task Manager.
Expected: CPU rata-rata di bawah 1%.

- [ ] **Step 4: Laporkan hasil**

Tulis hasil setiap langkah. Jika ada yang gagal atau tidak bisa diverifikasi di mesin ini (misalnya tidak ada laptop untuk brightness), tulis itu secara eksplisit.

---

## Di luar rencana ini

Fase 3 (notifikasi dengan MSIX dan timer) dan Fase 4 (installer, code signing, auto-update) akan dibuat sebagai rencana terpisah.
