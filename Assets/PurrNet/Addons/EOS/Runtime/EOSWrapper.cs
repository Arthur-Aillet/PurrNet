using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Platform;
using PurrNet.Logging;
using UnityEngine;

namespace PurrNet.EOS
{
    public static class EOSWrapper
    {
        public static ProductUserId LocalUserId;
        public static PlatformInterface Platform;
        public static bool IsReady => _initialized && LocalUserId != null;
        private static bool _initializing;
        private static bool _initialized;

        public static void Init(NetworkManager networkManager)
        {
            if (_initialized || _initializing) return;
            _initializing = true;
            networkManager.onTick += Tick;

            var options = new Options
            {
                ProductId = "da9e4dca850848f18b98da4c7e069335",
                SandboxId = "fb05dcc5221d45b296077d729acf659c",
                ClientCredentials = new ClientCredentials()
                {
                    ClientId = "xyza7891xSSsUnz4SXBYvwQ7Tjja8eVc",
                    ClientSecret = "WK5Dqdbo02KfUri/W+hFeIBXFFTpAtQQ2QY/+IzhQgA"
                },
                DeploymentId = "e631307e9cd1479eaa3ba4115a159ee1",
            };

            InitializeOptions platformOptions = new InitializeOptions
            {
                ProductName = "PurrNet",
                ProductVersion = "1.0",
                AllocateMemoryFunction = IntPtr.Zero,
            };

            PlatformInterface.Initialize(ref platformOptions);

            Platform = PlatformInterface.Create(ref options);

            if (Platform == null)
                throw new System.Exception("Failed to initialize EOS Platform.");
            
            var connect = Platform.GetConnectInterface();

            var createOptions = new CreateDeviceIdOptions
            {
                DeviceModel = Environment.MachineName,
            };

            connect.CreateDeviceId(ref createOptions, null, OnDeviceIdCreated);
        }
        
        private static void OnDeviceIdCreated(ref CreateDeviceIdCallbackInfo info)
        {
            Debug.Log($"On Device ID created status: {info.ResultCode}");
            if (info.ResultCode == Result.Success || info.ResultCode == Result.DuplicateNotAllowed)
            {
                var credentials = new Credentials
                {
                    Type = ExternalCredentialType.DeviceidAccessToken,
                    Token = null,
                };

                var loginOptions = new LoginOptions
                {
                    Credentials = credentials,
                    UserLoginInfo = new UserLoginInfo()
                    {
                        DisplayName = "Bobsi",
                    }
                };

                Platform.GetConnectInterface().Login(ref loginOptions, null, OnLoginComplete);
            }
            else
            {
                PurrLogger.LogError($"DeviceId creation failed: {info.ResultCode}");
            }
        }

        private static void OnLoginComplete(ref LoginCallbackInfo data)
        {
            Debug.Log($"Login complete status: {data.ResultCode} | LocalUser: {data.LocalUserId}");
            if (data.ResultCode == Result.Success)
            {
                LocalUserId = data.LocalUserId;
            }
            else if (data.ResultCode == Result.InvalidUser)
            {
                var createUserOptions = new CreateUserOptions
                {
                    ContinuanceToken = data.ContinuanceToken
                };
                Platform.GetConnectInterface().CreateUser(ref createUserOptions, null, OnUserCreated);
            }

            _initializing = false;
            _initialized = true;
        }

        private static void OnUserCreated(ref CreateUserCallbackInfo data)
        {
            if (data.ResultCode == Result.Success)
            {
                LocalUserId = data.LocalUserId;
            }
        }

        public static void Tick(bool asServer)
        {
            Platform?.Tick();
        }
    }
}
