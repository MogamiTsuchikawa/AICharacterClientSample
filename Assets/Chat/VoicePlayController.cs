using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace Chat
{
    public class VoicePlayController : MonoBehaviour
    {
        private List<AudioClip> _playWaitList = new();
        [SerializeField] 
        private AudioSource _characterAudioSource;

        [SerializeField] 
        private float trimStart;
        [SerializeField] 
        private float trimEnd;
        
        private float _audioStopTime;
        private bool _alreadyStopped = false;
        
        public void AddAudioClipToWaitList(AudioClip clip)
        {
            _playWaitList.Add(clip);
        }

        private void Update()
        {
            // ウエイトリストが 0 だったら, 入力待機状態に遷移

            if (_playWaitList.Count <= 0 && _alreadyStopped)
            {
                return;
            }
            
            
            
            // 最後から trimTiming の秒数は再生しない
            if (_characterAudioSource.isPlaying)
            {
                if (_characterAudioSource.time >= _audioStopTime)
                {
                    _characterAudioSource.Stop();

                    if (_playWaitList.Count <= 0)
                    {
                        _alreadyStopped = true;
                    }
                }
            }
            // 新しく再生する
            else
            {
                if (_playWaitList.Count <= 0)
                {
                    return;
                }
                _alreadyStopped = false;
                
                _characterAudioSource.clip = _playWaitList[0];
                _playWaitList.RemoveAt(0);
                
                // 終了時間をセット
                _audioStopTime = _characterAudioSource.clip.length - trimEnd;
                
                // 開始時刻をセットして再生
                _characterAudioSource.time = trimStart;
                _characterAudioSource.Play();
            }
        }
    }

}