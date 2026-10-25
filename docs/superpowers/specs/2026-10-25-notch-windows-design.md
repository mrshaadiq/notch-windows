# Notch Windows — Desain

Tanggal: 2026-10-25
Status: Disetujui secara konseptual, menunggu review spec tertulis

## 1. Tujuan

Aplikasi desktop Windows yang menampilkan "pill" kecil di tepi atas layar, mirip notch/Dynamic Island di macOS. Pill diam dalam keadaan biasa, melebar sebentar saat ada event, lalu kembali mengecil. Target rilis publik dan open source.

**Fitur MVP:**
- Notifikasi, volume, dan brightness
- Kontrol media (info lagu dan play/pause/next)
- Charging dan status baterai
- Timer / countdown

**Kriteria sukses:**
- Event sampai pill terlihat dalam ≤100 ms
- Animasi 60 fps di berbagai skala DPI
- Pill tidak mengambil fokus dari jendela yang sedang dipakai
- CPU rata-rata saat idle di bawah 1%

## 2. Non-tujuan (MVP)

- Menyembunyikan OSD volume/brightness bawaan Windows
- Dukungan brightness untuk monitor eksternal (butuh DDC/CI)
- Multi-monitor (hanya monitor utama)
- Timer yang bertahan setelah aplikasi ditutup
- Installer, code signing, dan auto-update (fase distribusi, setelah MVP)

## 3. Stack

- C# dengan WPF
- Target `net8.0-windows10.0.19041.0` atau lebih baru
- Windows 10 22H2 dan Windows 11
- Library NAudio untuk volume
- xUnit untuk tes

## 4. Arsitektur

Alur data satu arah: **Provider → EventBus → IslandController → PillWindow**.

| Komponen | Tanggung jawab |
|---|---|
| `PillWindow` (App) | Window WPF tanpa border, transparan, Topmost, tidak muncul di taskbar, `WS_EX_NOACTIVATE`. Hanya merender, tidak tahu fitur. |
| `EventBus` (Core) | Menerima `IslandEvent` dan menyiarkannya ke controller. |
| `IslandController` (Core) | Memegang antrean dan state `Idle → Compact → Expanded`. |
| `IEventProvider` (Core) | Kontrak provider: `Start()`, `Stop()`, event `Raised`. |
| Providers | Satu kelas per fitur. Tidak menyentuh UI. |
| `Native` | Pembungkus P/Invoke dan COM. |
| Tray icon (App) | Pengaturan, toggle idle, dan keluar. |

**Batas:** `Core` tidak bergantung pada Windows. `Providers` tidak menyentuh UI. `App` tidak memanggil API Windows secara langsung.

**Struktur solusi:**

```
NotchWindows.sln
src/
  NotchWindows.App/          Entry point WPF, DI, tray icon, PillWindow, views
  NotchWindows.Core/         IslandEvent, EventBus, IslandController, aturan antrean
  NotchWindows.Providers/    Satu folder per provider
  NotchWindows.Native/       P/Invoke dan COM (user32, Core Audio, WMI)
tests/
  NotchWindows.Core.Tests/
```

## 5. Model event

Ada dua lapisan tampilan:

- **Ambient (Compact):** status yang berjalan terus dan tampil kecil, misalnya ikon media saat musik main atau sisa waktu timer. Tidak masuk antrean.
- **Transient (Expanded):** event sesaat yang melebar, tampil sebentar, lalu kembali ke ambient.

`IslandEvent` berisi `Kind`, `Priority`, `Duration`, dan `Key`.

| Event | Prioritas | Durasi tampil | Catatan |
|---|---|---|---|
| Timer selesai | 100 | Sampai di-dismiss | Satu-satunya yang tidak auto-collapse |
| Notifikasi | 80 | 4 dtk | Coalesce per aplikasi |
| Charging / baterai | 60 | 3 dtk | Hanya saat status berubah |
| Volume / brightness | 40 | 2 dtk | `Key` sama, event diganti, bukan ditumpuk |
| Ganti lagu | 20 | 2,5 dtk | Dari media session |

**Aturan antrean:**
- Hanya satu event yang expanded. Prioritas lebih tinggi langsung menggantikan yang sedang tampil. Event yang tergantikan masuk antrean jika sisa durasinya lebih dari 1 dtk, selain itu dibuang.
- Antrean maksimal 5. Jika penuh, event prioritas terendah yang paling lama dibuang.
- Event dengan `Key` yang sama menggantikan entri lama dan mereset timer tampil.
- Saat kursor berada di atas pill, auto-collapse ditunda. Timer lanjut saat kursor pergi.

## 6. Provider dan API Windows

- **Media:** `GlobalSystemMediaTransportControlsSessionManager` (WinRT). Mendengarkan perubahan sesi dan properti media, serta mengontrol play/pause/next. Aplikasi yang tidak mendaftar ke SMTC tidak terlihat.
- **Volume:** Core Audio `IAudioEndpointVolume` lewat NAudio, dengan callback perubahan.
- **Brightness:** WMI (`WmiMonitorBrightness`), polling ringan sekitar 1 Hz. Hanya laptop.
- **Notifikasi:** `UserNotificationListener`. Butuh izin pengguna dan package identity (MSIX). Saat development harus dijalankan sebagai paket.
- **Baterai:** `GetSystemPowerStatus` (P/Invoke) dan `SystemEvents.PowerModeChanged`. Persentase dicek ulang setiap 30 dtk.
- **Timer:** logika murni di `Providers` dengan `DispatcherTimer`.

Setiap API Windows dibungkus interface (`IVolumeSource`, `IBrightnessSource`, `IMediaSessionSource`, `IPowerStatusSource`) supaya provider bisa dites dengan fake.

## 7. Animasi

- Tiga state: `Idle`, `Compact`, `Expanded`.
- **Expand:** lebar dan tinggi ease-out sekitar 250 dtk. Konten fade-in setelah jeda 80 dtk.
- **Collapse:** sekitar 200 dtk ease-in. Konten fade-out dulu, baru bentuknya mengecil.
- **Ganti event saat expanded:** bentuk tetap, hanya konten crossfade sekitar 120 dtk.
- Animasi dijalankan lewat satu `Border` dengan `Storyboard`.
- Provider berjalan di background thread dan mengirim event ke dispatcher UI lewat bus.

## 8. DPI dan monitor

- Manifest `PerMonitorV2`. Ukuran pill dihitung dalam DIP.
- Posisi: top-center dari **bounds** monitor utama (bukan work area), sehingga pill menempel di tepi atas.
- Posisi dihitung ulang saat `SystemEvents.DisplaySettingsChanged` dan saat DPI berubah.
- Multi-monitor (pindah ke monitor aktif) ditunda.

## 9. Mode fullscreen

**Default:** pill disembunyikan saat ada aplikasi fullscreen, game, film, atau presentasi. Dicek lewat `SHQueryUserNotificationState`. Pill tetap Topmost. Perilaku ini bisa diubah lewat pengaturan.

## 10. Idle

**Default:** saat tidak ada ambient dan tidak ada event, pill tampil sebagai bar panjang yang tipis di tepi atas, seperti tompel panjang. Bar ini tidak menampilkan konten. Lebar dan tingginya tetap dan bisa diatur lewat pengaturan. Bisa dimatikan sepenuhnya lewat pengaturan.

## 11. Penanganan error

- Provider yang melempar exception ditangkap oleh host provider, di-log, lalu dinonaktifkan untuk sesi itu setelah 3 kali gagal beruntun. Fitur lain tetap jalan.
- Izin yang ditolak membuat provider berstatus `Unavailable`. Pengaturan menampilkan petunjuk, tanpa popup.
- Hardware yang tidak didukung membuat provider tidak didaftarkan saat startup.
- Exception yang tidak tertangkap dicatat dan shell tetap hidup.
- Log di `%LOCALAPPDATA%\NotchWindows\logs`, dirotasi harian. Isi notifikasi tidak di-log secara default.

## 12. Tes

- **`Core.Tests`:** prioritas, preemption, batas antrean, coalescing per `Key`, dan durasi tampil. Waktu diabstraksikan lewat `IClock`.
- **Providers:** dites dengan fake untuk interface sumber.
- **Native:** tes manual.
- **Checklist manual per rilis:** Windows 10 22H2 dan 11, DPI 100/125/150/200%, multi-monitor, tema terang dan gelap.
- **CI:** GitHub Actions `windows-latest` untuk build dan `Core.Tests`.

## 13. Target responsivitas

- Event sampai pill terlihat ≤100 ms, diukur dengan `Stopwatch` di overlay debug.
- Animasi 60 fps. Thumbnail album art di-cache.
- CPU idle rata-rata di bawah 1%. Polling hanya untuk brightness dan persentase baterai.

## 14. Fase

1. **Fase 1:** `Core`, `PillWindow`, `IslandController`, dan tray icon dengan provider dummy.
2. **Fase 2:** provider volume, brightness, media, dan baterai.
3. **Fase 3:** notifikasi (butuh MSIX) dan timer.
4. **Fase 4 (distribusi):** installer, code signing, dan auto-update.

## 15. Risiko

- Izin notifikasi dan keharusan MSIX.
- Brightness monitor eksternal.
- Aplikasi media yang tidak mendukung SMTC.
- OSD bawaan Windows tetap muncul berdampingan dengan pill.
