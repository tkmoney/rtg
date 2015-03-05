using UnityEngine;
using System.Collections;
using SimpleJSON;
using System.Collections.Generic;

public class RottenTomatoesService {
	public string apiKey = "tm88ysp974864gwnx578au2p";
	private JSONNode json;
	public int resultLimit = 4;
	public JSONNode moviesJson;
	

	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {

	}

	
	public IEnumerator GetTopRentalsAsync(){
		string result = "";
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		sb.Append ("http://api.rottentomatoes.com/api/public/v1.0/lists/dvds/top_rentals.json?limit=");
		sb.Append (resultLimit.ToString());
		sb.Append("&apikey=");
		sb.Append(apiKey);

		WWW www = new WWW(sb.ToString());
		yield return www;
		Debug.Log ("GetTopRentalsAsync after callback");
		json = JSON.Parse(www.text);
		moviesJson = json;
	}


	IEnumerator LoadUrl(string url, System.Action<string> callback){
		string result;
		WWW www = new WWW( url );
		float elapsedTime = 0.0f;
		
		while (!www.isDone) {
			elapsedTime += Time.deltaTime;
			if (elapsedTime >= 10.0f) break;
			yield return null;
		}
		
		if (!www.isDone || !string.IsNullOrEmpty (www.error)) {
			callback(null);
			yield break;
		}
		
		result = www.text;


		callback (result);
	}

}







