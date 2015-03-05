using System.Collections;
using ExitGames.Client.Photon;
using ExitGames.Client.Photon.Lite;
using ExitGames.Client.Photon.LoadBalancing;
using System.Collections.Generic;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using Random = UnityEngine.Random;
using System;

public class GameClient : LoadBalancingClient {

	private byte MaxPlayers = 4;
	private static string[] RoomPropsInLobby = new string[]{ "t#" };


	
	public override void OnOperationResponse(OperationResponse operationResponse)
	{
		Debug.Log ("operationResponse: ");
		Debug.Log (operationResponse);
		base.OnOperationResponse(operationResponse);
		// this.DebugReturn(DebugLevel.ERROR, operationResponse.ToStringFull());    // log as ERROR to make sure it's not filtered out due to log level
		
		switch (operationResponse.OperationCode)
		{
		case (byte)OperationCode.WebRpc:
			Debug.Log ("WebRpc response");
			Debug.Log("WebRpc-Response: " + operationResponse.ToStringFull());
			if (operationResponse.ReturnCode == 0)
			{
				this.OnWebRpcResponse(new WebRpcResponse(operationResponse));
			}
			break;
		case (byte)OperationCode.JoinGame:
		case (byte)OperationCode.ChangeGroups:
		case (byte)OperationCode.CreateGame:
			if (this.Server == ServerConnection.GameServer)
			{
				if (operationResponse.ReturnCode == 0)
				{
					//this.UpdateBoard();
				}
			}
			break;
		case (byte)OperationCode.JoinRandomGame:
			Debug.Log ("JoinRandomGame");

			if (operationResponse.ReturnCode == ErrorCode.NoRandomMatchFound)
			{
				// no room found: we create one!
				//this.CreateTurnbasedRoom(new Hashtable());
			}
			break;
		}
	}

	private void OnWebRpcResponse(WebRpcResponse response)
	{
		GameObject gamelistObject = GameObject.Find("GamesList");


		if (response.ReturnCode != 0)
		{
			Debug.Log(response.ToStringFull());     // in an error case, it's often helpful to see the full response
			return;
		}
		
		if (response.Name.Equals("GetGameList"))
		{
			//this.SavedGames.Clear();

			foreach (Transform child in gamelistObject.transform) {// clear list
				GameObject.Destroy(child.gameObject);
			}

			if (response.Parameters == null)
			{
				Debug.Log("WebRpcResponse for GetGameList contains no rooms: " + response.ToStringFull());
				return;
			}

			List<Dictionary<string,string>> roomList = new List<Dictionary<string, string>>();

			// the response for GetGameList contains a Room's name as Key and another Dictionary<string,object> with the values the web service sends
			foreach (KeyValuePair<string, object> pair in response.Parameters)
			{
				// per key (room name), we send 
				// "ActorNr" which is the PlayerId/ActorNumber this user had in the room
				// "Properties" which is another Dictionary<string,object> with the properties that the lobby sees
				Dictionary<string, object> roomValues = pair.Value as Dictionary<string, object>;
				
				int savedActorNumber = (int)roomValues["ActorNr"];
				Dictionary<string, object> savedRoomProps = roomValues["Properties"] as Dictionary<string, object>; // we are not yet using these in this demo
				
				//this.SavedGames.Add(pair.Key, savedActorNumber);
				Debug.Log(pair.Key + " actorNr: " + savedActorNumber + " props: " + SupportClass.DictionaryToString(savedRoomProps));

				Dictionary<string,string> dic = new Dictionary<string, string>();
				dic.Add("id", (string)pair.Key);
				roomList.Add(dic);
			}
			gamelistObject.GetComponent<GamesList>().addGamesListItems(roomList);
		}
	}
	
	public void CreateTurnbasedRoom(Hashtable customRoomProps)
	{
		Debug.Log (this.LocalPlayer.CustomProperties);
		//string newRoomName = this.PlayerName + "-" +Random.Range(0,999).ToString("D4");    // for int, Random.Range is max-exclusive!
		//Debug.Log("CreateTurnbasedRoom() will create: " + newRoomName);
		
		RoomOptions demoRoomOptions = new RoomOptions()
		{
			MaxPlayers = MaxPlayers,
			CustomRoomPropertiesForLobby = RoomPropsInLobby,
			CustomRoomProperties = customRoomProps,
			PlayerTtl = int.MaxValue
		};
		
		this.OpCreateRoom(GetUniqueID(), demoRoomOptions,TypedLobby.Default);
	}

	public static string GetUniqueID(){
		string key = "ID";
		
		var random = new System.Random();                     
		DateTime epochStart = new System.DateTime(1970, 1, 1, 8, 0, 0, System.DateTimeKind.Utc);
		double timestamp = (System.DateTime.UtcNow - epochStart).TotalSeconds;
		
		string uniqueID = "-"+String.Format("{0:X}", Convert.ToInt32(timestamp))                //Time
				+"-"+String.Format("{0:X}", Convert.ToInt32(Time.time*1000000))        //Time in game
				+"-"+String.Format("{0:X}", random.Next(1000000000));                //random number
		
		Debug.Log("Generated Unique ID: "+uniqueID);

		
		return uniqueID;
	}

}
