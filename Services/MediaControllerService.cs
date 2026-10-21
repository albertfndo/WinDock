using System;
using System.Diagnostics;
using System.Windows.Threading;
using WinDock.Native;

namespace WinDock.Services
{
    /// <summary>
    /// Service monitoring active media playback (Spotify, browsers with YouTube/Soundcloud,
    /// Apple Music, VLC, Windows Media Player) and dispatching global media key controls.
    /// </summary>
    public class MediaControllerService
    {
        private static MediaControllerService? _instance;
        public static MediaControllerService Instance => _instance ??= new MediaControllerService();

        private readonly DispatcherTimer _pollTimer;

        public bool IsPlaying { get; private set; }
        public string TrackTitle { get; private set; } = string.Empty;
        public string ArtistName { get; private set; } = string.Empty;
        public string AppSource { get; private set; } = string.Empty;

        public event Action<MediaControllerService>? MediaStateChanged;

        public bool IsRunning => _pollTimer.IsEnabled;

        public MediaControllerService()
        {
            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _pollTimer.Tick += (s, e) => UpdateMediaState();
        }

        public void Start()
        {
            if (_pollTimer.IsEnabled) return;

            UpdateMediaState();
            _pollTimer.Start();
        }

        public void Stop()
        {
            if (!_pollTimer.IsEnabled) return;

            _pollTimer.Stop();
        }

        /// <summary>
        /// Sends virtual media Play/Pause toggle key.
        /// </summary>
        public void PlayPause()
        {
            try
            {
                NativeMethods.keybd_event(NativeMethods.VK_MEDIA_PLAY_PAUSE, 0, 0, 0);
                NativeMethods.keybd_event(NativeMethods.VK_MEDIA_PLAY_PAUSE, 0, NativeMethods.KEYEVENTF_KEYUP, 0);

                // Optimistic UI toggle for immediate responsiveness
                IsPlaying = !IsPlaying;
                MediaStateChanged?.Invoke(this);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaControllerService] PlayPause error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends virtual media Next Track key.
        /// </summary>
        public void NextTrack()
        {
            try
            {
                NativeMethods.keybd_event(NativeMethods.VK_MEDIA_NEXT_TRACK, 0, 0, 0);
                NativeMethods.keybd_event(NativeMethods.VK_MEDIA_NEXT_TRACK, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaControllerService] NextTrack error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends virtual media Previous Track key.
        /// </summary>
        public void PreviousTrack()
        {
            try
            {
                NativeMethods.keybd_event(NativeMethods.VK_MEDIA_PREV_TRACK, 0, 0, 0);
                NativeMethods.keybd_event(NativeMethods.VK_MEDIA_PREV_TRACK, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaControllerService] PreviousTrack error: {ex.Message}");
            }
        }

        /// <summary>
        /// Scans active media windows across running processes and extracts track metadata.
        /// </summary>
        public void UpdateMediaState()
        {
            Process[]? processes = null;
            try
            {
                processes = Process.GetProcesses();

                bool found = false;
                string newTitle = string.Empty;
                string newArtist = string.Empty;
                string newSource = string.Empty;

                // Priority 1: Spotify
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.ProcessName.Equals("spotify", StringComparison.OrdinalIgnoreCase))
                        {
                            string title = p.MainWindowTitle;
                            if (!string.IsNullOrWhiteSpace(title) &&
                                !title.Equals("Spotify Free", StringComparison.OrdinalIgnoreCase) &&
                                !title.Equals("Spotify Premium", StringComparison.OrdinalIgnoreCase) &&
                                !title.Equals("Spotify", StringComparison.OrdinalIgnoreCase) &&
                                title.Contains(" - "))
                            {
                                int sepIndex = title.IndexOf(" - ", StringComparison.Ordinal);
                                if (sepIndex > 0)
                                {
                                    newArtist = title.Substring(0, sepIndex).Trim();
                                    newTitle = title.Substring(sepIndex + 3).Trim();
                                    newSource = "Spotify";
                                    found = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Process access or exit exception
                    }
                }

                // Priority 2: Browsers with YouTube / SoundCloud
                if (!found)
                {
                    foreach (var p in processes)
                    {
                        try
                        {
                            string pName = p.ProcessName.ToLowerInvariant();
                            if (pName is "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi")
                            {
                                string title = p.MainWindowTitle;
                                if (string.IsNullOrWhiteSpace(title)) continue;

                                if (title.Contains("- YouTube", StringComparison.OrdinalIgnoreCase))
                                {
                                    string clean = title;
                                    int ytIndex = clean.LastIndexOf("- YouTube", StringComparison.OrdinalIgnoreCase);
                                    if (ytIndex >= 0)
                                    {
                                        clean = clean.Substring(0, ytIndex).Trim();
                                    }

                                    // Strip leading notification badge counts like "(1) " or "(99+) "
                                    if (clean.StartsWith("(") && clean.Contains(") "))
                                    {
                                        int closeIdx = clean.IndexOf(") ");
                                        if (closeIdx is > 0 and < 8)
                                        {
                                            clean = clean.Substring(closeIdx + 2).Trim();
                                        }
                                    }

                                    if (clean.Contains(" - "))
                                    {
                                        int sepIdx = clean.IndexOf(" - ", StringComparison.Ordinal);
                                        newArtist = clean.Substring(0, sepIdx).Trim();
                                        newTitle = clean.Substring(sepIdx + 3).Trim();
                                    }
                                    else
                                    {
                                        newTitle = clean;
                                        newArtist = "YouTube";
                                    }

                                    newSource = "YouTube";
                                    found = true;
                                    break;
                                }
                                else if (title.Contains("SoundCloud", StringComparison.OrdinalIgnoreCase))
                                {
                                    string clean = title;
                                    if (clean.Contains("| SoundCloud", StringComparison.OrdinalIgnoreCase))
                                    {
                                        int scIdx = clean.LastIndexOf("| SoundCloud", StringComparison.OrdinalIgnoreCase);
                                        clean = clean.Substring(0, scIdx).Trim();
                                    }
                                    else if (clean.Contains("- SoundCloud", StringComparison.OrdinalIgnoreCase))
                                    {
                                        int scIdx = clean.LastIndexOf("- SoundCloud", StringComparison.OrdinalIgnoreCase);
                                        clean = clean.Substring(0, scIdx).Trim();
                                    }

                                    if (clean.Contains(" by ", StringComparison.OrdinalIgnoreCase))
                                    {
                                        int byIdx = clean.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
                                        newTitle = clean.Substring(0, byIdx).Trim();
                                        newArtist = clean.Substring(byIdx + 4).Trim();
                                    }
                                    else if (clean.Contains(" - "))
                                    {
                                        int sepIdx = clean.IndexOf(" - ", StringComparison.Ordinal);
                                        newArtist = clean.Substring(0, sepIdx).Trim();
                                        newTitle = clean.Substring(sepIdx + 3).Trim();
                                    }
                                    else
                                    {
                                        newTitle = clean;
                                        newArtist = "SoundCloud";
                                    }

                                    newSource = "SoundCloud";
                                    found = true;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Ignored
                        }
                    }
                }

                // Priority 3: Apple Music / iTunes
                if (!found)
                {
                    foreach (var p in processes)
                    {
                        try
                        {
                            string pName = p.ProcessName.ToLowerInvariant();
                            if (pName is "applemusic" or "itunes" or "music.ui")
                            {
                                string title = p.MainWindowTitle;
                                if (!string.IsNullOrWhiteSpace(title) &&
                                    !title.Equals("Apple Music", StringComparison.OrdinalIgnoreCase) &&
                                    !title.Equals("iTunes", StringComparison.OrdinalIgnoreCase) &&
                                    title.Contains(" - "))
                                {
                                    int sepIdx = title.IndexOf(" - ", StringComparison.Ordinal);
                                    newArtist = title.Substring(0, sepIdx).Trim();
                                    newTitle = title.Substring(sepIdx + 3).Trim();
                                    newSource = "Apple Music";
                                    found = true;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Ignored
                        }
                    }
                }

                // Priority 4: VLC / Generic Media
                if (!found)
                {
                    foreach (var p in processes)
                    {
                        try
                        {
                            string pName = p.ProcessName.ToLowerInvariant();
                            if (pName is "vlc")
                            {
                                string title = p.MainWindowTitle;
                                if (!string.IsNullOrWhiteSpace(title) &&
                                    !title.Equals("VLC media player", StringComparison.OrdinalIgnoreCase))
                                {
                                    string clean = title.Replace(" - VLC media player", "").Trim();
                                    if (clean.Contains(" - "))
                                    {
                                        int sepIdx = clean.IndexOf(" - ", StringComparison.Ordinal);
                                        newArtist = clean.Substring(0, sepIdx).Trim();
                                        newTitle = clean.Substring(sepIdx + 3).Trim();
                                    }
                                    else
                                    {
                                        newTitle = clean;
                                        newArtist = "VLC Media";
                                    }

                                    newSource = "VLC";
                                    found = true;
                                    break;
                                }
                            }
                            else if (pName is "wmplayer")
                            {
                                string title = p.MainWindowTitle;
                                if (!string.IsNullOrWhiteSpace(title) &&
                                    !title.Equals("Windows Media Player", StringComparison.OrdinalIgnoreCase))
                                {
                                    newTitle = title;
                                    newArtist = "Media Player";
                                    newSource = "Media Player";
                                    found = true;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Ignored
                        }
                    }
                }

                bool stateChanged = (IsPlaying != found) ||
                                    (TrackTitle != newTitle) ||
                                    (ArtistName != newArtist) ||
                                    (AppSource != newSource);

                IsPlaying = found;
                TrackTitle = newTitle;
                ArtistName = newArtist;
                AppSource = newSource;

                if (stateChanged)
                {
                    MediaStateChanged?.Invoke(this);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaControllerService] Scan error: {ex.Message}");
            }
            finally
            {
                if (processes != null)
                {
                    foreach (var p in processes)
                    {
                        p.Dispose();
                    }
                }
            }
        }
    }
}
