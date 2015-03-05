using ExitGames.Client.Photon.LoadBalancing;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using Svelto.Tasks;
using System.Collections;
using System;
using Facebook.MiniJSON;


public class GameUI : MonoBehaviour {
	public GameClient GameClient;
	private Game GameInstance;
	private SerialTaskCollection st;

	private GameObject LobbyUi;
	private GameObject GameUi;
	private GameObject DisconnectedUi;
	private GameObject ConnectingUi;
	private GameObject ResultsUi;
	private GameObject AuthUi;
	private AuthenticationValues AuthValues;

	private bool isJoiningExistingGame = false;
	private string region = "us";
	private GameObject gameplayUi;
	private GameplayUI gamePlayUiScript;

	void Awake(){
		FB.Init (onFbinit, onHideUnity);
	}

	void onFbinit ()
	{
		if (FB.IsLoggedIn) {
			Debug.Log("logged in!!");
			// connect to Master server
			GameClient.ConnectToRegionMaster(region);  // INIT CONNECT
		} else {
			Debug.Log("not logged in!!");
			//show auth screen
			SwitchUiAuth();
		}
	}
	
	
	void onHideUnity(bool isGameShown){
		
		if(!isGameShown){
			
		}else{
			
		}
		
	}

	// Use this for initialization
	void Start () {
		AuthValues = new AuthenticationValues ();
		AuthValues.AuthType = CustomAuthenticationType.Facebook;
		Application.runInBackground = true;
		LobbyUi = GameObject.Find ("Lobby");
		GameUi = GameObject.Find ("Game");
		DisconnectedUi = GameObject.Find ("Disconnected");
		ConnectingUi = GameObject.Find ("Connecting");
		gameplayUi = GameObject.Find ("gameplay");
		ResultsUi = GameObject.Find ("GameOver");
		AuthUi = GameObject.Find ("Auth");
		gamePlayUiScript = gameplayUi.GetComponent<GameplayUI> ();
		LobbyUi.SetActive (false);
		GameUi.SetActive (false);
		DisconnectedUi.SetActive (false);
		ConnectingUi.SetActive (false);

		GameClient = new GameClient();

		GameClient.AppId = "fdc885b4-203e-4726-90fe-e3a6fc442b4e";
		GameClient.AppVersion = "1.0";
		GameClient.OnStateChangeAction += OnStateChanged;
		Debug.Log ("name");
	}
	
	// Update is called once per frame
	void Update () {
		GameClient.Service ();
		//Debug.Log (GameClient.State);
	}

	void SwitchUiToLobby ()
	{
		ConnectingUi.SetActive (false);
		DisconnectedUi.SetActive (false);
		GameUi.SetActive (false);
		AuthUi.SetActive (false);
		LobbyUi.SetActive (true);
		ResultsUi.SetActive(false);

	}

	void SwitchUiToGame ()
	{
		AuthUi.SetActive (false);
		ConnectingUi.SetActive (false);
		DisconnectedUi.SetActive (false);
		LobbyUi.SetActive (false);
		GameUi.SetActive (true);
		ResultsUi.SetActive(false);

	}

	void SwitchUiToDisconnected ()
	{
		AuthUi.SetActive (false);
		ConnectingUi.SetActive (false);
		DisconnectedUi.SetActive (true);
		GameUi.SetActive (false);
		LobbyUi.SetActive (false);
		ResultsUi.SetActive(false);

	}

	void SwitchUiToConnecting ()
	{
		AuthUi.SetActive (false);
		DisconnectedUi.SetActive (false);
		GameUi.SetActive (false);
		LobbyUi.SetActive (false);
		ConnectingUi.SetActive (true);
		ResultsUi.SetActive(false);

	}

	void SwitchUiToGameOver(){
		AuthUi.SetActive (false);
		DisconnectedUi.SetActive (false);
		GameUi.SetActive (false);
		LobbyUi.SetActive (false);
		ConnectingUi.SetActive (false);
		ResultsUi.SetActive(true);
	}

	void SwitchUiAuth(){
		AuthUi.SetActive (true);
		DisconnectedUi.SetActive (false);
		GameUi.SetActive (false);
		LobbyUi.SetActive (false);
		ConnectingUi.SetActive (false);
		ResultsUi.SetActive(false);
	}
	
	void OnStateChanged (ClientState obj)
	{
		Debug.Log (GameClient.State);
		if(obj.ToString().Contains("Connecting")){
			SwitchUiToConnecting();
		}else{
			switch (obj) {
			case ClientState.JoinedLobby:
				SwitchUiToLobby();
				break;
			case ClientState.Joined:
				if(isJoiningExistingGame){
					//setUpGameFromRoomProps();
				}
				SwitchUiToGame();
				break;
			case ClientState.Disconnected:
				SwitchUiToDisconnected();
				break;
			}
		}

	}

	public void OnApplicationQuit()
	{
		if (this.GameClient != null)
		{
			this.GameClient.Disconnect();
		}
		this.GameClient = null;
	}
	
	public void ConnectToRegionMaster(){
		GameClient.ConnectToRegionMaster(region);
	}

	public void setupNewGame(){
		//if (GameClient.PlayerName.Length > 0) {
			GameInstance = new Game();
			GameInstance.OnCreationComplete += HandleNewGameCreationComplete;
		//}
	}

	public void setUpGameFromRoomProps(){
		GameInstance = new Game(GameInstance.customRoomProperties);
		GameInstance.OnCreationComplete += HandleOnCreationComplete;
	}

	
	public void joinExistingGame(string roomid){
		isJoiningExistingGame = true;
		GameClient.OpJoinRoom (roomid);
	}
	
	void HandleOnCreationComplete (object sender)//used for joining existing game
	{
		Debug.Log ("HandleOnCreationComplete");
		GameInstance.OnCreationComplete -= HandleOnCreationComplete;
		isJoiningExistingGame = false;
		setupGameRoundUi ();
	}

	void HandleNewGameCreationComplete (object sender)
	{
		Debug.Log ("HandleOnCreationComplete. create room");
		GameClient.CreateTurnbasedRoom (GameInstance.customRoomProperties);
		GameInstance.OnCreationComplete -= HandleNewGameCreationComplete;
		setupGameRoundUi ();
	}

	private void setupGameRoundUi(){
		gamePlayUiScript.setUiForRound (GameInstance.currentRound);
	}

	public void GoToNextRound(){

		if (GameInstance.rounds [GameInstance.rounds.Count - 1] == GameInstance.currentRound) {// on last round
			GameInstance.gameOver = true;
			SwitchUiToGameOver();
		} else {
			Round r = GameInstance.GetNextRound ();
			setupGameRoundUi ();
		}

	}

	public void GetRoomsList()
	{
		GameClient.OpWebRpc("GetGameList", new Dictionary<string, object>());
	}

	public void LeaveRoomPermanet(string roomName)
	{
		Dictionary<string, object> rpcParams = new Dictionary<string, object> ();
		rpcParams.Add ("GameId", roomName);
		GameClient.OpWebRpc("GameLeave", rpcParams);
	}

	public void LeaveRoom(){
		GameClient.OpLeaveRoom(true);
	}

	public void JoinRoomByName(string name){
		GameClient.OpJoinRoom (name,true);
	}

	public void FbLogin(){
		FB.Login ("id, email", FBLoginCallback);
	}

	void FBLoginCallback (FBResult result)
	{
		if (FB.IsLoggedIn) {
			Debug.Log ("fb login success");
			// set facebook auth creds for future connection
			AuthValues.SetAuthParameters(FB.UserId, FB.AccessToken);
			GetFacebookDataForUser();
		} else {
			Debug.Log("fb login failed");
		}
	}

	private void GetFacebookDataForUser ()
	{
		bool haveFacebookData = false;
		bool havePic = false;
		bool haveName = false;
		string picUrl = "";
		string name = "";

		FB.API (Util.GetPictureURL("me", 128, 128), Facebook.HttpMethod.GET, delegate(FBResult result) {
			if(result.Error != null){
				FB.Logout();
			}else{
				Debug.Log("pic return");
				picUrl = Util.DeserializePictureURLString(result.Text);
			}

			if(picUrl != "" && name != ""){
				DoneGettingFbData(picUrl,name);
			}

		});
		FB.API ("me?fields=name", Facebook.HttpMethod.GET, delegate(FBResult result) {
			if(result.Error != null){
				FB.Logout();
			}else{
				Debug.Log("name return");
				Dictionary<string,object> d = Json.Deserialize(result.Text) as Dictionary<string,object>;
				name = (string)d["name"];
			}

			if(picUrl != "" && name != ""){
				DoneGettingFbData(picUrl,name);
			}
			
		});

	}

	private void DoneGettingFbData(string picUrl, string name){
		GameClient.PlayerName = name;
		Hashtable playerProps = new Hashtable ();
		playerProps.Add("pic_url", picUrl);
		GameClient.LocalPlayer.SetCustomProperties(playerProps);
		GameClient.OpJoinLobby (FB.UserId, LobbyType.AsyncRandomLobby);
		//GameClient.ConnectToRegionMaster(region);  // INIT CONNECT
		
	}


}
