using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class GameplayUI : MonoBehaviour {
	private GameObject coroutineObj;
	private Coroutines coroutines;
	private GameObject yearTxt;
	private GameObject titleTxt;
	private GameObject criticScoreTxt;
	private GameObject audienceScoreTxt;
	private GameObject posterObj;
	private Texture2D posterTexture;

	// Use this for initialization
	void Start () {
		coroutineObj = GameObject.Find ("Coroutine");
		yearTxt = GameObject.Find ("MovieYear");
		titleTxt = GameObject.Find ("MovieTitle");
		criticScoreTxt = GameObject.Find ("CriticScore");
		audienceScoreTxt = GameObject.Find ("AudienceScore");
		posterObj = GameObject.Find ("Poster");
		coroutines = coroutineObj.GetComponent<Coroutines> ();

	}

	public void setUiForRound(Round round){
		yearTxt.GetComponent<Text> ().text = round.year.Replace("\"","");
		titleTxt.GetComponent<Text> ().text = round.title.Replace("\"","");
		criticScoreTxt.GetComponent<Text> ().text = (round.criticsScore.ToString() + "%");
		audienceScoreTxt.GetComponent<Text> ().text = (round.audienceScore.ToString() + "%");

		coroutines.getPoster (round.poster);
	}
	

}
