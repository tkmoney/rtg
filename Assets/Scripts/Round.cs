using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Round
{
	public readonly string title;
	public readonly string year;
	public Texture2D posterTexture;
	public readonly string criticsRating;
	public readonly string audienceRating;
	public readonly int criticsScore;
	public readonly int audienceScore;
	public readonly string poster;
	private Dictionary<int, int> PlayerGuesses;

	public Round(Dictionary<string,string> roundData){


		title = roundData["title"];
		criticsRating = roundData["critics_rating"];
		audienceRating = roundData["audience_rating"];
		criticsScore = int.Parse(roundData["critics_score"]);
		year = roundData["year"];

		audienceScore = int.Parse(roundData["audience_score"]);
		poster = roundData["poster"];
	}

	
	public void AddPlayerGuess(int playerId, int playerGuess){
		PlayerGuesses.Add (playerId, playerGuess);
	}


}

