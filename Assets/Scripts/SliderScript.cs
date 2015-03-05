using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class SliderScript : MonoBehaviour {

	public Slider slider;
	public GameObject percentTxt;
	private Text txt;

	// Use this for initialization
	void Start () {
		txt = percentTxt.GetComponent<Text> ();
	}


	public void OnSliderValueChange(){
		txt.text = (slider.value + "%");
	}
}
