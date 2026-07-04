using LiveKit.Internal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LiveKit
{
    /// <summary>
    /// Feeds rendered playback audio into the APM reverse stream.
    /// </summary>
    internal sealed class ApmReverseStream : System.IDisposable
    {
        private readonly AudioBuffer _captureBuffer = new();
        private readonly AudioProcessingModule _apm;
        private AudioProbe _probe;
        private volatile bool _isActive;

        internal ApmReverseStream(AudioProcessingModule apm)
        {
            _apm = apm;
        }

        internal void Start()
        {
            if (_probe != null)
            {
                _isActive = true;
                return;
            }

            AudioListener audioListener = Object.FindAnyObjectByType<AudioListener>();
            if (audioListener == null)
            {
                Utils.Error("AudioListener not found in scene, reverse AEC stream is unavailable.");
                return;
            }

            _probe = audioListener.gameObject.GetComponent<AudioProbe>();
            if (_probe == null)
                _probe = audioListener.gameObject.AddComponent<AudioProbe>();

            _probe.AudioRead += OnAudioRead;
            _isActive = true;
        }

        internal void Stop()
        {
            _isActive = false;
            if (_probe == null)
                return;

            _probe.AudioRead -= OnAudioRead;
            _probe = null;
        }

        public void Dispose()
        {
            Stop();
            _captureBuffer.Dispose();
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            if (!_isActive)
            {
                _captureBuffer.Clear();
                return;
            }

            if (!FfiClient.IsOperational)
            {
                HandleFfiShutdownDuringAudioCallback();
                return;
            }

            _captureBuffer.Write(data, (uint)channels, (uint)sampleRate);
            while (true)
            {
                using AudioFrame frame = _captureBuffer.ReadDuration(AudioProcessingModule.FrameDurationMs);
                if (frame == null)
                    break;

                try
                {
                    _apm.ProcessReverseStream(frame);
                }
                catch (System.Exception ex) when (FfiClient.ShouldIgnoreShutdownException(ex))
                {
                    HandleFfiShutdownDuringAudioCallback();
                    break;
                }
                catch (System.Exception ex)
                {
                    Utils.Error($"Reverse audio processing failed: {ex.Message}");
                    break;
                }
            }
        }

        private void HandleFfiShutdownDuringAudioCallback()
        {
            _isActive = false;
            _captureBuffer.Clear();
        }
    }
}
