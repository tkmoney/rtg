using UnityEngine;
using System.Collections;
using Svelto.Tasks;
using SimpleJSON;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using System.Collections.Generic;

public class Game{
	public delegate void GameCreationComplete(object sender);
	public event GameCreationComplete OnCreationComplete;
	const Hashtable defaultRoomProps = null;
	private int numOfRounds;
	private bool createFromRoomProps;
	public int currentRoundIndex = 0;
	public Round currentRound;
	public List<Round> rounds;
	public bool gameOver;
	public Hashtable customRoomProperties;
	private RottenTomatoesService RtApiService;
	private SerialTaskCollection st;
	private JSONArray ja;

	public Game(Hashtable roomProps = defaultRoomProps){
		gameOver = false;
		st = new SerialTaskCollection();
		st.onComplete += AfterInit;
		RtApiService = new RottenTomatoesService ();

		if (roomProps != null && roomProps.Count > 0) {
			createFromRoomProps = true;
			customRoomProperties = roomProps;
			AfterInit();
		} else {
			createFromRoomProps = false;
			InitRTRequest();
		}
	}

	private void AfterInit ()
	{
		Debug.Log ("AfterInit");
		createRounds ();
		currentRoundIndex = 0;
		currentRound = rounds[currentRoundIndex];
		if(OnCreationComplete == null){
			return;
		}
		OnCreationComplete (this);
	}

	public Round GetNextRound(){
		currentRoundIndex = currentRoundIndex + 1;
		currentRound = rounds[currentRoundIndex];
		return currentRound;
	}

	private void createRounds(){
		Debug.Log("createRounds");
		rounds = new List<Round> ();
		foreach (DictionaryEntry de in customRoomProperties) {//for each round
			Dictionary<string,string> dic = (Dictionary<string,string>)de.Value;
			Round r = new Round(dic);
			rounds.Add(r);
		}

	}

	IEnumerator CreateRoomProps(){
		Debug.Log ("CreateRoomProps!");
		JSONNode json = RtApiService.moviesJson;
		moviesJsonToRoomProps (json["movies"].AsArray);
		yield return null;
	}
	

	private void InitRTRequest(){
		Debug.Log ("InitRTRequest");
		st.Add (RtApiService.GetTopRentalsAsync());
		st.Add (CreateRoomProps());
		TaskRunner.Instance.RunManaged(st);
	}

	
	void moviesJsonToRoomProps (JSONArray json)
	{
		Hashtable customRoomProps = new Hashtable ();
		int index = 0;
		foreach (JSONNode n in json){
			Dictionary<string, string> dic = new Dictionary<string, string>();
			dic.Add("title",n["title"].ToString());
			dic.Add("year",n["year"].ToString());
			dic.Add("critics_rating",n["ratings"]["critics_rating"].ToString());
			dic.Add("audience_rating",n["ratings"]["audience_rating"].ToString());
			dic.Add("critics_score",n["ratings"]["critics_score"].Value.ToString());
			dic.Add("audience_score",n["ratings"]["audience_score"].Value.ToString());
			dic.Add("poster",n["posters"]["thumbnail"].Value.ToString());

			customRoomProps.Add("r#".Replace("#",index.ToString()), dic);

			index++;
		}
		numOfRounds = index;
		customRoomProperties = customRoomProps;
	}
}
