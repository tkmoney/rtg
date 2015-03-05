using UnityEngine;
using System.Collections;

public class navigation : MonoBehaviour {

	// Use this for initialization
	void Start () {
		Application.runInBackground = true;
	}
	
	// Update is called once per frame
	void Update () {
	
	}

	public void goToScene(string scenename){
		Application.LoadLevel (scenename);
	}

}
