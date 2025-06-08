using System;
using System.Collections.Generic;
using PurrNet;
using TriInspector;
using UnityEngine;

public class Test : NetworkIdentity
{
    [SerializeField] private int _localHealth = 100;
    [SerializeField] private SyncDictionary<int, int> _dictionary = new ();

    private void Awake()
    {
        _dictionary.onChanged += OnListChanged;
    }

    public GameObject gecko1;
    public GameObject gecko2;
    public GameObject gecko3;
    public GameObject gecko4;
    public int price1;
    public int price2;
    public int price3;
    public int price4;

    public string resultEventName = "N SEND";

    [ObserversRpc(
        PurrNet.Transports.Channel.ReliableUnordered,
        false, true, false, false, true,
        PurrNet.CompressionLevel.None, 0F
    )]
    private void SendToAllClientsInternal(
        GameObject firstGecko,
        GameObject secondGecko,
        GameObject thirdGecko,
        GameObject fourthGecko,
        int firstPrice,
        int secondPrice,
        int thirdPrice,
        int fourthPrice
    )
    {
        gecko1 = firstGecko;
        gecko2 = secondGecko;
        gecko3 = thirdGecko;
        gecko4 = fourthGecko;
        price1 = firstPrice;
        price2 = secondPrice;
        price3 = thirdPrice;
        price4 = fourthPrice;
    }

    [Button]
    public void SendToAllClients()
    {
        SendToAllClientsInternal(
            gecko1, gecko2, gecko3, gecko4,
            price1, price2, price3, price4
        );
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        _dictionary.onChanged -= OnListChanged;
    }

    private void OnListChanged(SyncDictionaryChange<int, int> change)
    {
        Debug.Log($"{change} | Tick: {networkManager.tickModule.syncedTick}");
    }

    private void Update()
    {
        if(Input.GetKeyDown(KeyCode.X))
            SetHealth(_localHealth - 10);
        if (Input.GetKeyDown(KeyCode.C))
            TestList();
    }

    private void TestList()
    {
        if(!_dictionary.TryAdd(0, 0))
            _dictionary[0] += 1;
        if(!_dictionary.TryAdd(1, 0))
            _dictionary[1] += 1;
        if(!_dictionary.TryAdd(2, 0))
            _dictionary[2] += 1;
        if(!_dictionary.TryAdd(3, 0))
            _dictionary[3] += 1;

        _dictionary.Remove(0);
    }

    [ObserversRpc(bufferLast: true)]
    private void SetHealth(int newHealth)
    {
        _localHealth = newHealth;
    }
}
