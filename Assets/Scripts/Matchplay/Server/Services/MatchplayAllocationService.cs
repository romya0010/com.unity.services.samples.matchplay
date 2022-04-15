using System;
using System.Text;
using System.Threading.Tasks;
using Matchplay.Shared;
using Newtonsoft.Json;
using Unity.Services.Matchmaker.Models;
using Unity.Services.Multiplay;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Matchplay.Server
{
    public class MatchplayAllocationService : IDisposable
    {
        IMultiplayService m_MultiplayService;
        MultiplayEventCallbacks m_Servercallbacks;
        IServerCheckManager m_ServerCheckManager;
        IServerEvents m_ServerEvents;
        string m_AllocationId;

        const string k_PayloadProxyUrl = "http://localhost:8086";

        public async Task<MatchmakerAllocationPayload> BeginServerAndAwaitMatchmakerAllocation()
        {
            m_AllocationId = null;
            m_MultiplayService = Unity.Services.Multiplay.MultiplayService.Instance;
            m_Servercallbacks = new MultiplayEventCallbacks();
            m_Servercallbacks.Allocate += OnMultiplayAllocation;

            m_ServerEvents = await m_MultiplayService.SubscribeToServerEventsAsync(m_Servercallbacks);

            Debug.Log("Awaiting Multiplay Allocation");
            var mmPayload = await AwaitMatchmakerPayload();
            return mmPayload;
        }

        public async Task BeginServerCheck(GameInfo info)
        {
            m_ServerCheckManager = await m_MultiplayService.ConnectToServerCheckAsync((ushort)info.MaxUsers, "Matchplay Server", info.gameMode.ToString(), "0", info.map.ToString());
        }

        //The networked server is our source of truth for what is going on, so we update our multiplay check server with values from there.

        //Wait for the allocation to be called back before continuing
        async Task<MatchmakerAllocationPayload> AwaitMatchmakerPayload()
        {
            var config = m_MultiplayService.ServerConfig;

            if (config.AllocatedUuid != null && m_AllocationId == null)
            {
                Debug.Log("Already had Allocation ID at start.");
                m_AllocationId = config.AllocatedUuid;
            }

            while (m_AllocationId == null)
            {
                await Task.Delay(100);
            }

            return await GetMatchmakerAllocationPayloadAsync(m_AllocationId);
        }

        void OnMultiplayAllocation(MultiplayAllocation allocation)
        {
            m_AllocationId = allocation.AllocationId;
        }

        void OnMultiplayDeAllocation(MultiplayDeallocation deallocation)
        {
            Debug.Log($"Multiplay Deallocated : ID: {deallocation.AllocationId}\nEvent: {deallocation.EventId}\nServer{deallocation.ServerId}");
        }

        void OnMultiplayError(MultiplayError error)
        {
            Debug.Log($"MultiplayError : {error.Reason}\n{error.Detail}");
        }

        /// <summary>
        /// This should be in the SDK but we can use web-requests to get access to the MatchmakerAllocationPayload
        /// </summary>
        /// <param name="allocationID"></param>
        /// <returns></returns>
        async Task<MatchmakerAllocationPayload> GetMatchmakerAllocationPayloadAsync(string allocationID)
        {
            Debug.Log($"Getting Allocation Payload with ID: {allocationID}");
            var payloadUrl = k_PayloadProxyUrl + $"/payload/{allocationID}";
            using (var webRequest = UnityWebRequest.Get(payloadUrl))
            {
                var operation = webRequest.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Delay(50);
                }

                Debug.Log($"Web Request Text:{operation.webRequest.downloadHandler.text}");

                switch (webRequest.result)
                {
                    case UnityWebRequest.Result.ConnectionError:
                        Debug.LogError(nameof(GetMatchmakerAllocationPayloadAsync) + ": ConnectionError: " + webRequest.error);
                        break;
                    case UnityWebRequest.Result.DataProcessingError:
                        Debug.LogError(nameof(GetMatchmakerAllocationPayloadAsync) + ": Error: " + webRequest.error);
                        break;
                    case UnityWebRequest.Result.ProtocolError:
                        Debug.LogError(nameof(GetMatchmakerAllocationPayloadAsync) + ": HTTP Error: " + webRequest.error);
                        break;
                    case UnityWebRequest.Result.Success:
                        Debug.Log(nameof(GetMatchmakerAllocationPayloadAsync) + ":\nReceived: " + webRequest.downloadHandler.text);
                        break;
                    case UnityWebRequest.Result.InProgress:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                return JsonConvert.DeserializeObject<MatchmakerAllocationPayload>(webRequest.downloadHandler.text);
            }
        }

        public void SetPlayerCount(ushort count)
        {
            m_ServerCheckManager.CurrentPlayers = count;
        }

        public void AddPlayer()
        {
            m_ServerCheckManager.CurrentPlayers += 1;
        }

        public void RemovePlayer()
        {
            m_ServerCheckManager.CurrentPlayers -= 1;
        }

        public void ChangedMap(Map newMap)
        {
            if (m_ServerCheckManager?.Map == null)
                return;
            m_ServerCheckManager.Map = newMap.ToString();
        }

        public void ChangedMode(GameMode mode)
        {
            if (m_ServerCheckManager?.GameType == null)
                return;
            m_ServerCheckManager.GameType = mode.ToString();
        }

        public void Dispose()
        {
            if (m_Servercallbacks != null)
            {
                m_Servercallbacks.Allocate -= OnMultiplayAllocation;
                m_Servercallbacks.Deallocate -= OnMultiplayDeAllocation;
                m_Servercallbacks.Error -= OnMultiplayError;
            }

            m_ServerEvents?.UnsubscribeAsync();
        }
    }

    [Serializable]
    public class MatchmakerAllocationPayload
    {
        public MatchProperties MatchProperties;
        public string QueueName;
        public string PoolName;
        public string BackfillTicketId;

        public override string ToString()
        {
            StringBuilder payloadDescription = new StringBuilder();
            payloadDescription.AppendLine("Matchmaker Allocation Payload:");
            payloadDescription.AppendFormat("-QueueName: {0}\n", QueueName);
            payloadDescription.AppendFormat("-PoolName: {0}\n", PoolName);
            payloadDescription.AppendFormat("-ID: {0}\n", BackfillTicketId);
            payloadDescription.AppendFormat("-Teams: {0}\n", MatchProperties.Teams.Count);
            payloadDescription.AppendFormat("-Players: {0}\n", MatchProperties.Players.Count);
            payloadDescription.AppendFormat("-Region: {0}\n", MatchProperties.Region);
            return payloadDescription.ToString();
        }
    }
}