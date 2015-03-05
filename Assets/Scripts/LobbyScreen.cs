﻿using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class LobbyScreen : MonoBehaviour {

	public GameUI GameUi;
	public RawImage profileImg;
	public Text name;

	// Use this for initialization
	void Start () {
		//profileImg = GameObject.Find("LobbyPlayerImage").GetComponent<RawImage>();
		//text = GameObject.Find ("LobbyPlayerName").GetComponent<Text>();
	}

	void OnEnable(){
		Debug.Log ("OnEnable!");


		if(GameUi.GameClient != null){
			string profileUrl = (string)GameUi.GameClient.LocalPlayer.CustomProperties["pic_url"];
			Debug.Log(profileUrl);

			name.text = GameUi.GameClient.PlayerName;
			if(profileUrl != null){
				StartCoroutine(setAndApplyProfilePic(profileUrl));
			}
		}


	}
	private IEnumerator setAndApplyProfilePic(string url){
		WWW www = new WWW (url);
		yield return www;
		profileImg.texture = www.texture;
	}

}