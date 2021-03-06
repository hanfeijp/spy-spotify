﻿using System;
using System.Threading;
using NAudio.Lame;
using SpotifyAPI.Local;

namespace EspionSpotify
{
    internal class Watcher
    {
        public static bool Running;
        public static bool Ready = true;
        public int CountSecs;

        private Recorder _recorder;
        private Song _currentSong;
        private bool _isPlaying;
        private Song _lastKnownSong;

        private readonly FrmEspionSpotify _form;
        private readonly LAMEPreset _bitrate;
        private readonly Recorder.Format _format;
        private readonly VolumeWin _sound;

        private readonly bool _bCdTrack;
        private readonly bool _bNumFile;
        private readonly bool _strucDossiers;
        private readonly int _minTime;
        private readonly string _path;
        private readonly string _charSeparator;

        private const bool Mute = true;
        private const int NextSongEventMaxEstimatedDelay = 5;

        public int NumTrack { get; private set; }

        private bool NumTrackActivated => NumTrack != -1;
        private bool AdPlaying => _currentSong.IsAd;
        private string SongTitle => _currentSong.ToString();
        private bool NormalSongPlaying => _currentSong.IsNormal;
        private bool RecorderUpAndRunning => _recorder != null && _recorder.Running;
        private bool SongIsStillPlaying => _lastKnownSong.Equals(_currentSong);
        private bool IsOldSong => _currentSong.CurrentLength > _currentSong.Length - NextSongEventMaxEstimatedDelay;

        public Watcher(FrmEspionSpotify espionSpotifyForm, string path, LAMEPreset bitrate,
            Recorder.Format format, VolumeWin sound, int minTime, bool strucDossiers, 
            string charSeparator, bool bCdTrack, bool bNumFile, int cdNumTrack)
        {
            if (path == null) path = "";

            _form = espionSpotifyForm;
            _path = path;
            _bitrate = bitrate;
            _format = format;
            _sound = sound;
            NumTrack = bCdTrack || bNumFile ? cdNumTrack : -1;
            _minTime = minTime;
            _strucDossiers = strucDossiers;
            _charSeparator = charSeparator;
            _bCdTrack = bCdTrack;
            _bNumFile = bNumFile;
        }

        private void OnPlayStateChanged(object sender, PlayStateEventArgs e)
        {
            var playing = e.Playing;
            var song = Spotify.Instance.GetStatus().Track;

            if (playing == _isPlaying) return;
            _isPlaying = playing;

            if (song != null)
            {
                _currentSong = new Song(song);
                _lastKnownSong = new Song();
            }

            if (_isPlaying)
            {
                _sound.SetSpotifyToMute(AdPlaying);
                _form.UpdatePlayingTitle(SongTitle);
            }
            else
            {
                _form.UpdatePlayingTitle("Spotify");
            }

            _form.UpdateIconSpotify(_isPlaying);
        }

        private void OnTrackChanged(object sender, TrackChangeEventArgs e)
        {
            if (RecorderUpAndRunning && IsOldSong) _sound.SleepWhileTheSongEnds();

            var newTrack = e.NewTrack;
            if (newTrack == null ) return;

            var newSong = new Song(newTrack);
            if (_currentSong.Equals(newSong))
            {
                _form.UpdateIconSpotify(_isPlaying, RecorderUpAndRunning);
                return;
            }

            _currentSong = newSong;
            _lastKnownSong = new Song();

            _form.UpdatePlayingTitle(SongTitle);

            if (AdPlaying)
            {
                _form.WriteIntoConsole(FrmEspionSpotify.Rm.GetString($"logAdPlaying"));
            }

            _sound.SetSpotifyToMute(AdPlaying);
        }

        private void OnTrackTimeChanged(object sender, TrackTimeChangeEventArgs e)
        {
            if (_currentSong != null)
            {
                _currentSong.CurrentLength = e.TrackTime;
            }
        }

        private void SetSpotifyApi()
        {
            _currentSong = new Song();
            _lastKnownSong = new Song();

            if (SpotifyLocalAPI.IsSpotifyInstalled() && !SpotifyLocalAPI.IsSpotifyRunning())
            {
                _form.WriteIntoConsole(FrmEspionSpotify.Rm.GetString($"logSpotifyConnecting"));
            }

            Spotify.Connect();

            Spotify.Instance.ListenForEvents = true;
            Spotify.Instance.OnPlayStateChange += OnPlayStateChanged;
            Spotify.Instance.OnTrackChange += OnTrackChanged;
            Spotify.Instance.OnTrackTimeChange += OnTrackTimeChanged;
        }

        public void Run()
        {
            if (Running) return;

            Ready = false;
            Running = true;

            SetSpotifyApi();

            if (SpotifyLocalAPI.IsSpotifyRunning() && Spotify.IsConnected())
            {
                InitializeRecordingSession();

                while (Running)
                {
                    if (!SpotifyLocalAPI.IsSpotifyRunning())
                    {
                        _form.WriteIntoConsole(FrmEspionSpotify.Rm.GetString($"logSpotifyIsClosed"));
                        Running = false;
                    }
                    else if (!SongIsStillPlaying)
                    {
                        DoIKeepLastSong(RecorderUpAndRunning);
                        RecordSpotify();
                    }
                    _lastKnownSong = _currentSong;
                    Thread.Sleep(60);
                }

                DoIKeepLastSong(RecorderUpAndRunning);
                _form.WriteIntoConsole(FrmEspionSpotify.Rm.GetString($"logStoping"));
            }
            else if (SpotifyLocalAPI.IsSpotifyInstalled())
            {
                _form.WriteIntoConsole(FrmEspionSpotify.Rm.GetString($"logSpotifyNotConnected"));
            }
            else
            {
                _form.WriteIntoConsole(FrmEspionSpotify.Rm.GetString($"logSpotifyNotFound"));
            }

            EndRecordingSession();
        }

        private void RecordSpotify()
        {
            if (RecorderUpAndRunning || !_isPlaying || !NormalSongPlaying) return;

            _recorder = new Recorder(_form, _path, _bitrate, _format, _currentSong, _minTime, _strucDossiers,
                _charSeparator, _bCdTrack, _bNumFile, NumTrack);

            var recorderThread = new Thread(_recorder.Run);
            recorderThread.Start();

            _form.UpdateIconSpotify(_isPlaying, true);
            UpdateNumUp();
            CountSecs = 0;
        }

        private void InitializeRecordingSession()
        {
            _form.WriteIntoConsole(FrmEspionSpotify.Rm.GetString($"logStarting"));
            _sound.SetToHigh(Mute);

            var status = Spotify.Instance.GetStatus();
            _isPlaying = status.Playing;
            _form.UpdateIconSpotify(_isPlaying);

            var song = status.Track;
            if (song == null) return;
            _currentSong = new Song(song) { CurrentLength = status.PlayingPosition };

            _form.UpdatePlayingTitle(SongTitle);
            _sound.SetSpotifyToMute(AdPlaying);
        }

        private void EndRecordingSession()
        {
            _form.UpdateStartButton();
            _form.UpdatePlayingTitle("Spotify");
            Ready = true;
            _form.UpdateIconSpotify(false);
            _sound.SetSpotifyToMute(false);
            _sound.SetToHigh();
            _form.StopRecording();
        }

        private void DoIKeepLastSong(bool updateUi = false)
        {
            if (_recorder != null)
            {
                _recorder.Running = false;
                _recorder.Count = CountSecs;
                _form.UpdateIconSpotify(_isPlaying);
            }

            if (updateUi) UpdateNumDown();
        }

        private void UpdateNumDown()
        {
            if (!NumTrackActivated) return;

            if (CountSecs < _minTime) NumTrack--;
            _form.UpdateNum(NumTrack);
        }

        private void UpdateNumUp()
        {
            if (!NumTrackActivated) return;

            NumTrack++;
            _form.UpdateNum(NumTrack);
        }
    }
}
