using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GM : MonoSingleton<GM>
{
    public DataManager DM;
    public BattleManager BM;
    public UIManager UM;

    public void Start()
    {
        UM.Init();
        BM.Init();
    }
}
