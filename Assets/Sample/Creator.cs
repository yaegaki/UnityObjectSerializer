﻿using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class Creator : MonoBehaviour
{
    [SerializeField]
    private Hoge hoge = default;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            var f = new ScriptableObjectSerializer.JSONFormatter();
            var result = ScriptableObjectSerializer.Serializer.Serialize(this.hoge, f);
            var json = Encoding.UTF8.GetString(result);
            var h = ScriptableObjectSerializer.Serializer.Deserialize<Hoge>(result, f);
            Debug.Log(json);
            Debug.Log($"{h}:{h.fuu.aa}:{h.fuu.hoo.bb}");
        }
    }
}
