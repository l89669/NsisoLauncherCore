﻿using Endpoints;
using NsisoLauncherCore.Net.MojangApi.Api;
using NsisoLauncherCore.Net.MojangApi.Endpoints;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static NsisoLauncherCore.Net.MojangApi.Responses.AuthenticateResponse;
using static NsisoLauncherCore.Net.MojangApi.Responses.AuthenticateResponse.UserData;

namespace NsisoLauncherCore.Auth
{
    public class YggdrasilAuthenticator : IAuthenticator
    {
        public Credentials Credentials { get; set; }

        public Uri ProxyAuthServerAddress { get; set; }

        public List<string> AuthArgs { get; set; }

        public AuthenticateResult DoAuthenticate()
        {
            try
            {
                Authenticate authenticate = new Authenticate(Credentials);
                if (ProxyAuthServerAddress != null)
                {
                    authenticate.Address = ProxyAuthServerAddress;
                }
                if (AuthArgs != null && AuthArgs.Count != 0)
                {
                    authenticate.Arguments = AuthArgs;
                }
                var resultTask = authenticate.PerformRequestAsync();
                var result = resultTask.Result;
                if (result.IsSuccess)
                {
                    return new AuthenticateResult(AuthState.SUCCESS)
                    {
                        AccessToken = result.AccessToken,
                        UUID = result.SelectedProfile,
                        UserData = result.User
                    };
                }
                else
                {
                    AuthState errState;

                    if (result.Code == System.Net.HttpStatusCode.Forbidden)
                    { errState = AuthState.ERR_INVALID_CRDL; }
                    else if (result.Code == System.Net.HttpStatusCode.NotFound)
                    { errState = AuthState.ERR_NOTFOUND; }
                    else
                    { errState = AuthState.ERR_OTHER; }

                    return new AuthenticateResult(errState) { Error = result.Error };
                }
            }
            catch (Exception ex)
            {
                return new AuthenticateResult(AuthState.ERR_INSIDE) { Error = new Net.MojangApi.Error() { ErrorMessage = ex.Message, Exception = ex } };
            }
        }

        public async Task<AuthenticateResult> DoAuthenticateAsync()
        {
            try
            {
                Authenticate authenticate = new Authenticate(Credentials);
                if (ProxyAuthServerAddress != null)
                {
                    authenticate.Address = ProxyAuthServerAddress;
                }
                if (AuthArgs != null && AuthArgs.Count != 0)
                {
                    authenticate.Arguments = AuthArgs;
                }
                var result = await authenticate.PerformRequestAsync();

                if (result.IsSuccess)
                {
                    return new AuthenticateResult(AuthState.SUCCESS)
                    {
                        AccessToken = result.AccessToken,
                        UUID = result.SelectedProfile,
                        UserData = result.User
                    };
                }
                else
                {
                    AuthState errState;

                    if (result.Code == System.Net.HttpStatusCode.Forbidden)
                    { errState = AuthState.ERR_INVALID_CRDL; }
                    else if (result.Code == System.Net.HttpStatusCode.NotFound)
                    { errState = AuthState.ERR_NOTFOUND; }
                    else
                    { errState = AuthState.ERR_OTHER; }

                    return new AuthenticateResult(errState) { Error = result.Error };
                }
            }
            catch (Exception ex)
            {
                return new AuthenticateResult(AuthState.ERR_INSIDE) { Error = new Net.MojangApi.Error() { ErrorMessage = ex.Message, Exception = ex } };
            }
        }

        public YggdrasilAuthenticator(Credentials credentials)
        {
            this.Credentials = credentials;
        }
    }

    public class YggdrasilTokenAuthenticator : IAuthenticator
    {
        public string AccessToken { get; set; }

        public Uri ProxyAuthServerAddress { get; set; }

        public List<string> AuthArgs { get; set; }

        public Uuid UUID { get; set; }

        public UserData UserData { get; set; }

        public AuthenticateResult DoAuthenticate()
        {
            try
            {
                Validate validate = new Validate(AccessToken);
                if (ProxyAuthServerAddress != null)
                {
                    validate.Address = ProxyAuthServerAddress;
                }
                if (AuthArgs != null && AuthArgs.Count != 0)
                {
                    validate.Arguments = AuthArgs;
                }
                var resultTask = validate.PerformRequestAsync();
                var result = resultTask.Result;
                if (result.IsSuccess)
                {
                    return new AuthenticateResult(AuthState.SUCCESS) { AccessToken = this.AccessToken, UserData = this.UserData, UUID = this.UUID };
                }
                else
                {
                    AuthState state;
                    Refresh refresh = new Refresh(AccessToken);
                    if (ProxyAuthServerAddress != null)
                    {
                        refresh.Address = ProxyAuthServerAddress;
                    }
                    if (AuthArgs != null && AuthArgs.Count != 0)
                    {
                        refresh.Arguments = AuthArgs;
                    }
                    var refreshResultTask = refresh.PerformRequestAsync();
                    var refreshResult = refreshResultTask.Result;
                    if (refreshResult.IsSuccess)
                    {
                        this.AccessToken = refreshResult.AccessToken;
                        state = AuthState.SUCCESS;
                    }
                    else
                    {
                        state = AuthState.REQ_LOGIN;
                        if (refreshResult.Code == System.Net.HttpStatusCode.NotFound)
                        { state = AuthState.ERR_NOTFOUND; }
                    }
                    return new AuthenticateResult(state)
                    {
                        AccessToken = AccessToken = this.AccessToken,
                        UserData = this.UserData,
                        UUID = this.UUID,
                        Error = refreshResult.Error
                    };
                }
            }
            catch (Exception ex)
            {
                return new AuthenticateResult(AuthState.ERR_INSIDE) {
                    Error = new Net.MojangApi.Error() { ErrorMessage = ex.Message, Exception = ex },
                    AccessToken = AccessToken = this.AccessToken,
                    UserData = this.UserData,
                    UUID = this.UUID
                };
            }
        }

        public async Task<AuthenticateResult> DoAuthenticateAsync()
        {
            try
            {
                Validate validate = new Validate(AccessToken);
                if (ProxyAuthServerAddress != null)
                {
                    validate.Address = ProxyAuthServerAddress;
                }
                if (AuthArgs != null && AuthArgs.Count != 0)
                {
                    validate.Arguments = AuthArgs;
                }
                var result = await validate.PerformRequestAsync();
                if (result.IsSuccess)
                {
                    return new AuthenticateResult(AuthState.SUCCESS) { AccessToken = this.AccessToken, UserData = this.UserData, UUID = this.UUID };
                }
                else
                {
                    AuthState state;
                    Refresh refresh = new Refresh(AccessToken);
                    if (ProxyAuthServerAddress != null)
                    {
                        refresh.Address = ProxyAuthServerAddress;
                    }
                    if (AuthArgs != null && AuthArgs.Count != 0)
                    {
                        refresh.Arguments = AuthArgs;
                    }
                    var refreshResult = await refresh.PerformRequestAsync();
                    if (refreshResult.IsSuccess)
                    {
                        this.AccessToken = refreshResult.AccessToken;
                        state = AuthState.SUCCESS;
                    }
                    else
                    {
                        state = AuthState.REQ_LOGIN;
                        if (refreshResult.Code == System.Net.HttpStatusCode.NotFound)
                        { state = AuthState.ERR_NOTFOUND; }
                    }
                    return new AuthenticateResult(state)
                    {
                        AccessToken = AccessToken = this.AccessToken,
                        UserData = this.UserData,
                        UUID = this.UUID,
                        Error = refreshResult.Error
                    };
                }
            }
            catch (Exception ex)
            {
                return new AuthenticateResult(AuthState.ERR_INSIDE)
                {
                    Error = new Net.MojangApi.Error() { ErrorMessage = ex.Message, Exception = ex },
                    AccessToken = AccessToken = this.AccessToken,
                    UserData = this.UserData,
                    UUID = this.UUID
                };
            }
        }

        public YggdrasilTokenAuthenticator(string token, Uuid uuid, UserData userdata)
        {
            this.AccessToken = token;
            this.UUID = uuid;
            this.UserData = userdata;
        }
    }
}
