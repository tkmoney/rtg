using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class Coroutines : MonoBehaviour {
	private GameObject posterObj;
	
	// Use this for initialization
	void Start () {
		posterObj = GameObject.Find ("Poster");

	}
	
	// Update is called once per frame
	void Update () {
	
	}

	public void getPoster(string url){
		StartCoroutine(getAndApplyPoster(url));
	}

	public IEnumerator getAndApplyPoster(string url){
		WWW www = new WWW (url);
		yield return www;
		posterObj.GetComponent<RawImage>().texture = www.texture;
		
	}

}
