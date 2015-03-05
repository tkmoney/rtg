using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class LobbyGameListItem : MonoBehaviour {

	public string roomName = "";
	public GameUI GameUiClass;
	private GameObject roomIdTxtObj;
	private Text RoomIdTxt;
	
	// Use this for initialization
	void Start () {
		GameUiClass = GameObject.Find ("Scripts").GetComponent<GameUI> ();
		roomIdTxtObj = transform.Find("RoomId").gameObject;
		RoomIdTxt = roomIdTxtObj.GetComponent<Text> ();
		RoomIdTxt.text = roomName;
	}

	public void leaveRoomPerm(){
		GameUiClass.LeaveRoomPermanet (roomName);
		Destroy(this.gameObject);
	}

	public void joinRoom(){
		GameUiClass.JoinRoomByName (roomName);
	}

}
