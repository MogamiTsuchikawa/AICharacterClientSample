using UnityEngine;
using UnityEngine.UIElements;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine.Networking;

namespace Chat
{
    public class ChatController : MonoBehaviour
    {
        private string chatId;
        private const string BASE_URL = "http://localhost:8000";
        private static readonly HttpClient httpClient = new();
        [SerializeField]
        private UIDocument uiDocument;
        private VisualElement rootVisualElement;
        [SerializeField]
        private VoicePlayController voicePlayController;

        public static ChatController Instance { get; private set; }

        private async UniTask<AudioClip> DownloadWav(string wavId)
        {
            var wavUrl = $"{BASE_URL}/voice_wav/{wavId}.wav";
            using UnityWebRequest www =
                UnityWebRequestMultimedia.GetAudioClip(wavUrl, AudioType.WAV);
            await www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip downloadedClip = DownloadHandlerAudioClip.GetContent(www);
                return downloadedClip;
            }
            else
            {
                Debug.LogError($"Error downloading audio: {www.error}");
                throw new Exception($"Error downloading audio: {www.error}");
            }
        }
        
        async void Start()
        {
            Instance = this;
            rootVisualElement = uiDocument.rootVisualElement;

            chatId = await StartChat();
            var messageInput = 
                rootVisualElement.Q<TextField>("MessageInput");
            messageInput
                .RegisterCallback<KeyDownEvent>(OnMessageInputKeyDown);
        }
        // /start_chat に POST リクエストを送信して, チャットを開始する
        private async UniTask<string> StartChat()
        {
            string url = $"{BASE_URL}/start_chat";
            var content = new StringContent(
                JsonSerializer.Serialize(
                    new { character_id = "hoge" }), 
                Encoding.UTF8, 
                "application/json");

            try
            {
                HttpResponseMessage response = 
                    await httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();
                string responseBody = 
                    await response.Content.ReadAsStringAsync();
                var startChatResponse = 
                    JsonSerializer
                        .Deserialize<StartChatResponse>(responseBody);
                string chatId = startChatResponse.ChatId;
                Debug.Log($"Chat started with ID: {chatId}");
                return chatId;
            }
            catch (HttpRequestException e)
            {
                Debug.LogError($"Error starting chat: {e.Message}");
                return null;
            }
        }

        // ユーザーからの入力(message)を受け取り,
        // /chat/{chatId} に POST リクエストを送信する
        public async UniTask SendChatMessage(string message)
        {
            if (string.IsNullOrEmpty(chatId))
            {
                Debug.LogError("Chat has not been started yet.");
                return;
            }
            // UI周りの操作はメインスレッドで行う
            await UniTask.SwitchToMainThread();
            var _ = 
                new ChatMessageView(
                    "ユーザー", 
                    message, 
                    rootVisualElement);
            ChatMessageView aiMessage = 
                new("芝じい", "", rootVisualElement);
            await UniTask.SwitchToThreadPool();

            string url = $"{BASE_URL}/chat/{chatId}";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(
                JsonSerializer.Serialize(
                    new { content = message }), 
                Encoding.UTF8, 
                "application/json");
            string responseContent = "";
            using var response = await httpClient.SendAsync(
                request, 
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();

            var reader = new StreamReader(stream);
            // ServerSent-Eventsの形式でのStreamレスポンスを受け取る
            await foreach (var line in ReadLinesAsync(reader))
            {
                if (string.IsNullOrEmpty(line)) continue;
                // JSON文字列のレスポンスを
                // ChatResponseオブジェクトにデシリアライズ
                Debug.Log(line);
                
                var chatResponse = 
                    JsonSerializer.Deserialize<ChatResponse>(line);
                if (chatResponse.Status == "finished")
                {
                    break;
                }

                if (!string.IsNullOrEmpty(chatResponse.Content))
                {
                    responseContent += chatResponse.Content;
                    await UniTask.SwitchToMainThread();
                    if (!string.IsNullOrEmpty(chatResponse.Wav))
                    {
                        var clip = await DownloadWav(chatResponse.Wav);
                        voicePlayController.AddAudioClipToWaitList(clip);
                    }
                    aiMessage.Content = responseContent;
                    await UniTask.SwitchToThreadPool();
                }
                    
            }
        }

        // 音声データをサーバーに送信して文字起こしを行うメソッド
        public async UniTask SendChatMessageWithVoice(byte[] wavData)
        {
            string url = $"{BASE_URL}/voice2text";
            
            // MultipartFormDataContentを作成
            using var formData = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(wavData);
            formData.Add(audioContent, "file", "audio.wav");

            try
            {
                var response = await httpClient.PostAsync(url, formData);
                response.EnsureSuccessStatusCode();
                
                var responseBody = await response.Content.ReadAsStringAsync();
                var transcriptionResponse = JsonSerializer.Deserialize<TranscriptionResponse>(responseBody);
                await SendChatMessage(transcriptionResponse.Text);
            }
            catch (HttpRequestException e)
            {
                Debug.LogError($"Error sending voice data: {e.Message}");
            }
        }

        // レスポンスのモデル定義
        private class TranscriptionResponse
        {
            [JsonPropertyName("text")]
            public string Text { get; set; }
        }

        private static async IAsyncEnumerable<string> ReadLinesAsync(StreamReader reader)
        {
            while (!reader.EndOfStream)
            {
                yield return await reader.ReadLineAsync();
            }
        }

        private void OnMessageInputKeyDown(KeyDownEvent evt)
        {
            bool isWindows = 
                Application.platform == RuntimePlatform.WindowsPlayer || 
                Application.platform == RuntimePlatform.WindowsEditor;
            bool isMac = 
                Application.platform == RuntimePlatform.OSXPlayer || 
                Application.platform == RuntimePlatform.OSXEditor;
            // Win, Mac関係なく、
            // 修飾キーを押しながらEnterキーを押したことを判断する
            bool isEnterKeyPressed = 
                evt.keyCode == KeyCode.Return || 
                evt.keyCode == KeyCode.KeypadEnter;
            bool isModifierKeyPressed = 
                (isWindows && evt.ctrlKey) || 
                (isMac && evt.commandKey);

            if (isEnterKeyPressed && isModifierKeyPressed)
            {
                var messageInput = 
                    rootVisualElement.Q<TextField>("MessageInput");
                string message = messageInput.value;
                if (!string.IsNullOrWhiteSpace(message))
                {
                    // チャットメッセージを送信
                    SendChatMessage(message).Forget();
                    messageInput.value = string.Empty;
                }
                evt.StopPropagation();
            }
        }
        // /start_chat のJSONレスポンスのモデル定義
        private class StartChatResponse
        {
            [JsonPropertyName("chat_id")]
            public string ChatId { get; set; }
        }
        // /chat/{chatId} のJSONレスポンスのモデル定義
        private class ChatResponse
        {
            [JsonPropertyName("content")]
            public string Content { get; set; }
            [JsonPropertyName("status")]
            public string Status { get; set; }
            [JsonPropertyName("wav")]
            public string Wav { get; set; }
        }
    }
}