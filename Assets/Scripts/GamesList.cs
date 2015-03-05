using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GamesList : MonoBehaviour {

	public GameUI gameUi;

	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {
	
	}

	public void addGamesListItems(List<Dictionary<string,string>> roomList){

		foreach (Dictionary<string,string> dic in roomList) {
			GameObject instance = Instantiate(Resources.Load("GameListItem", typeof(GameObject))) as GameObject;
			instance.transform.SetParent(transform);
			instance.transform.localScale = new Vector3(1,1,1);
			LobbyGameListItem li = instance.GetComponent<LobbyGameListItem> ();
			li.GameUiClass = gameUi;
			li.roomName = dic["id"];

		}


	}
	
}
