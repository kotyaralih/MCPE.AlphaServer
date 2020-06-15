﻿using System;
using System.Collections.Generic;
using System.Text;

namespace MCPE.AlphaServer.Packets {
    public enum RakPacketType : byte {
        ConnectedPing = 0x00,
        ConnectedPong = 0x03,
        ConnectionRequest = 0x09,

        LoginRequest = 0x82,
        LoginResponse = 0x83,

        Ready = 0x84,
        Message = 0x85,
        SetTime = 0x86,
        StartGame = 0x87,
        AddMob = 0x88,
        AddPlayer = 0x89,
        RemovePlayer = 0x8A,
        AddEntity = 0x8C,
        RemoveEntity = 0x8D,
        AddItemEntity = 0x8E,
        TakeItemEntity = 0x8F,
        MoveEntity = 0x90,
        MoveEntityPosRot = 0x93,
        RotateHead = 0x94,
        MovePlayer = 0x95,
        PlaceBlock = 0x96,
        RemoveBlock = 0x97,
        UpdateBlock = 0x98,
        AddPainting = 0x99,
        Explode = 0x9A,
        LevelEvent = 0x9B,
        TileEvent = 0x9C,
        EntityEvent = 0x9D,
        RequestChunk = 0x9E,
        ChunkData = 0x9F,
        PlayerEquipment = 0xA0,
        PlayerArmorEquipment = 0xA1,
        Interact = 0xA2,
        UseItem = 0xA3,
        PlayerAction = 0xA4,
        HurtArmor = 0xA6,
        SetEntityData = 0xA7,
        SetEntityMotion = 0xA8,
        SetRiding = 0xA9,
        SetHealth = 0xAA,
        SetSpawnPosition = 0xAB,
        Animate = 0xAC,
        Respawn = 0xAD,
        SendInventory = 0xAE,
        DropItem = 0xAF,
        ContainerOpen = 0xB0,
        ContainerClose = 0xB1,
        ContainerSetSlot = 0xB2,
        ContainerSetData = 0xB3,
        ContainerSetContent = 0xB4,
        ContainerAck = 0xB5,
        Chat = 0xB6,
        SignUpdate = 0xB7,
        AdventureSettings = 0xB8,

        ConnectionRequestAccepted = 0x10,
        NewIncomingConnection = 0x13,
    }
}
