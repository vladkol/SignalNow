TURN REST Authentication
===========================================================

We suggest [REST API based authentication for TURN](http://tools.ietf.org/html/draft-uberti-behave-turn-rest-00) servers ([supported](https://github.com/coturn/coturn/wiki/turnserver#turn-rest-api) by coturn). Also see
<https://www.ietf.org/proceedings/87/slides/slides-87-behave-10.pdf>

TURN credentials are made as

**usercombo** -\> "*timestamp*:*turn_user*"

**turn user -\> usercombo**

**turn password -\>** base64_encode(hmac-sha1(input = **usercombo**, key = **secret_key**))

**timestamp** - token expiration time (seconds since 01/01/1970 UTC - UNIX time,
C-function call *time(NULL)*)


**turn_user** - company name or tenant id

**secret_key** must be configured with the function app and shared with TURN server (**use-auth-secret** option in coturn, with **static-auth-secret**={*secret_key*}*,* e.g. static-auth-secret=45mnGeR34\$sd8a) 

## Azure Function app configuration values 
* TURNAuthorizationKey - secret key used as auth secret for TURN server 
* TURNServersList - comma-separated list of TRUN URIs, e.g. turn:1.2.3.4:9991?transport=udp,turn:1.2.3.4:9992?transport=tcp


Example of TURN authentication tokes as returned from **Negotiate** function:

 
```
{
    "username" : "12334939**:**df\$df\#S8oT",
    "password" : "Fgdfadfsaf534Flsjflds=",
    "uris" : [
        "turn:1.2.3.4:9991?transport=udp",
        "turn:1.2.3.4:9992?transport=tcp",
        "turns:1.2.3.4:443?transport=tcp"
    ]
}
```

 
