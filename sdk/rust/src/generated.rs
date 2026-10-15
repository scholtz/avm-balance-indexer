#[allow(unused_imports)]
use progenitor_client::{encode_path, ClientHooks, OperationInfo, RequestBuilderExt};
#[allow(unused_imports)]
pub use progenitor_client::{ByteStream, ClientInfo, Error, ResponseValue};
/// Types used as operation parameters and responses.
#[allow(clippy::all)]
pub mod types {
    ///`BalanceResponse`
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct BalanceResponse {
        pub account: ::std::string::String,
        pub amount: ::std::string::String,
        #[serde(rename = "indexedRound")]
        pub indexed_round: u64,
        pub round: u64,
        pub token: TokenRef,
    }

    impl BalanceResponse {
        pub fn builder() -> builder::BalanceResponse {
            Default::default()
        }
    }

    ///The chain a deployment indexes.
    #[derive(
        :: serde :: Deserialize,
        :: serde :: Serialize,
        Clone,
        Copy,
        Debug,
        Eq,
        Hash,
        Ord,
        PartialEq,
        PartialOrd,
    )]
    pub enum ChainId {
        #[serde(rename = "algorand-mainnet")]
        AlgorandMainnet,
        #[serde(rename = "algorand-testnet")]
        AlgorandTestnet,
        #[serde(rename = "voi")]
        Voi,
    }

    impl ::std::fmt::Display for ChainId {
        fn fmt(&self, f: &mut ::std::fmt::Formatter<'_>) -> ::std::fmt::Result {
            match *self {
                Self::AlgorandMainnet => f.write_str("algorand-mainnet"),
                Self::AlgorandTestnet => f.write_str("algorand-testnet"),
                Self::Voi => f.write_str("voi"),
            }
        }
    }

    impl ::std::str::FromStr for ChainId {
        type Err = self::error::ConversionError;
        fn from_str(value: &str) -> ::std::result::Result<Self, self::error::ConversionError> {
            match value {
                "algorand-mainnet" => Ok(Self::AlgorandMainnet),
                "algorand-testnet" => Ok(Self::AlgorandTestnet),
                "voi" => Ok(Self::Voi),
                _ => Err("invalid value".into()),
            }
        }
    }

    impl ::std::convert::TryFrom<&str> for ChainId {
        type Error = self::error::ConversionError;
        fn try_from(value: &str) -> ::std::result::Result<Self, self::error::ConversionError> {
            value.parse()
        }
    }

    impl ::std::convert::TryFrom<::std::string::String> for ChainId {
        type Error = self::error::ConversionError;
        fn try_from(
            value: ::std::string::String,
        ) -> ::std::result::Result<Self, self::error::ConversionError> {
            value.parse()
        }
    }

    ///`EffectDto`
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct EffectDto {
        #[serde(rename = "balanceAfter")]
        pub balance_after: ::std::string::String,
        #[serde(deserialize_with = "::std::option::Option::deserialize")]
        pub counterparty: ::std::option::Option<::std::string::String>,
        pub delta: ::std::string::String,
        #[serde(rename = "innerPath")]
        pub inner_path: ::std::vec::Vec<i32>,
        #[serde(rename = "intraRoundOrder")]
        pub intra_round_order: i32,
        pub ordinal: i32,
        pub round: u64,
        #[serde(rename = "transactionId")]
        pub transaction_id: ::std::string::String,
        #[serde(rename = "type")]
        pub type_: ::std::string::String,
    }

    impl EffectDto {
        pub fn builder() -> builder::EffectDto {
            Default::default()
        }
    }

    ///The body of an error response: a message, and for some errors extra
    /// fields that name the offending values.
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct ErrorResponse {
        pub error: ::std::string::String,
    }

    impl ErrorResponse {
        pub fn builder() -> builder::ErrorResponse {
            Default::default()
        }
    }

    ///`HistoryResponse`
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct HistoryResponse {
        pub account: ::std::string::String,
        #[serde(rename = "indexedRound")]
        pub indexed_round: u64,
        pub items: ::std::vec::Vec<EffectDto>,
        #[serde(
            rename = "nextCursor",
            deserialize_with = "::std::option::Option::deserialize"
        )]
        pub next_cursor: ::std::option::Option<::std::string::String>,
        pub token: TokenRef,
    }

    impl HistoryResponse {
        pub fn builder() -> builder::HistoryResponse {
            Default::default()
        }
    }

    ///`HolderDto`
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct HolderDto {
        pub account: ::std::string::String,
        pub amount: ::std::string::String,
        #[serde(rename = "lastChangedRound")]
        pub last_changed_round: u64,
        #[serde(rename = "optedIn")]
        pub opted_in: bool,
    }

    impl HolderDto {
        pub fn builder() -> builder::HolderDto {
            Default::default()
        }
    }

    ///`HoldersResponse`
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct HoldersResponse {
        #[serde(rename = "indexedRound")]
        pub indexed_round: u64,
        pub items: ::std::vec::Vec<HolderDto>,
        #[serde(
            rename = "nextCursor",
            deserialize_with = "::std::option::Option::deserialize"
        )]
        pub next_cursor: ::std::option::Option<::std::string::String>,
        pub round: u64,
        pub token: TokenRef,
    }

    impl HoldersResponse {
        pub fn builder() -> builder::HoldersResponse {
            Default::default()
        }
    }

    ///`HoldingDto`
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct HoldingDto {
        pub amount: ::std::string::String,
        #[serde(rename = "lastChangedRound")]
        pub last_changed_round: u64,
        #[serde(rename = "optedIn")]
        pub opted_in: bool,
        pub token: TokenRef,
    }

    impl HoldingDto {
        pub fn builder() -> builder::HoldingDto {
            Default::default()
        }
    }

    ///`HoldingsResponse`
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct HoldingsResponse {
        pub account: ::std::string::String,
        #[serde(rename = "indexedRound")]
        pub indexed_round: u64,
        pub items: ::std::vec::Vec<HoldingDto>,
        #[serde(
            rename = "nextCursor",
            deserialize_with = "::std::option::Option::deserialize"
        )]
        pub next_cursor: ::std::option::Option<::std::string::String>,
    }

    impl HoldingsResponse {
        pub fn builder() -> builder::HoldingsResponse {
            Default::default()
        }
    }

    ///`ScanHoldersResponse`
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct ScanHoldersResponse {
        pub complete: bool,
        pub id: ::uuid::Uuid,
        pub items: ::std::vec::Vec<HolderDto>,
        #[serde(
            rename = "nextCursor",
            deserialize_with = "::std::option::Option::deserialize"
        )]
        pub next_cursor: ::std::option::Option<::std::string::String>,
        pub round: u64,
        pub state: ::std::string::String,
    }

    impl ScanHoldersResponse {
        pub fn builder() -> builder::ScanHoldersResponse {
            Default::default()
        }
    }

    ///`ScanRequest`
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug, Default)]
    pub struct ScanRequest {
        #[serde(
            rename = "includeZero",
            skip_serializing_if = "::std::option::Option::is_none"
        )]
        pub include_zero: ::std::option::Option<bool>,
        #[serde(skip_serializing_if = "::std::option::Option::is_none")]
        pub round: ::std::option::Option<u64>,
    }

    impl ScanRequest {
        pub fn builder() -> builder::ScanRequest {
            Default::default()
        }
    }

    ///`ScanResponse`
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct ScanResponse {
        pub complete: bool,
        #[serde(rename = "createdAt")]
        pub created_at: ::chrono::DateTime<::chrono::offset::Utc>,
        #[serde(deserialize_with = "::std::option::Option::deserialize")]
        pub error: ::std::option::Option<::std::string::String>,
        #[serde(rename = "expiresAt")]
        pub expires_at: ::chrono::DateTime<::chrono::offset::Utc>,
        #[serde(rename = "holdersFound")]
        pub holders_found: i64,
        pub id: ::uuid::Uuid,
        #[serde(rename = "includeZero")]
        pub include_zero: bool,
        pub round: u64,
        pub state: ::std::string::String,
        pub token: TokenRef,
        #[serde(rename = "updatedAt")]
        pub updated_at: ::chrono::DateTime<::chrono::offset::Utc>,
    }

    impl ScanResponse {
        pub fn builder() -> builder::ScanResponse {
            Default::default()
        }
    }

    ///Indexed round, target round, lag and ingestion state of the chain this
    /// deployment serves.
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct StatusResponse {
        pub chain: ::std::string::String,
        #[serde(
            rename = "indexedRound",
            deserialize_with = "::std::option::Option::deserialize"
        )]
        pub indexed_round: ::std::option::Option<u64>,
        #[serde(deserialize_with = "::std::option::Option::deserialize")]
        pub lag: ::std::option::Option<u64>,
        pub provider: ::std::string::String,
        pub state: ::std::string::String,
        #[serde(
            rename = "targetRound",
            deserialize_with = "::std::option::Option::deserialize"
        )]
        pub target_round: ::std::option::Option<u64>,
        #[serde(
            rename = "updatedAt",
            deserialize_with = "::std::option::Option::deserialize"
        )]
        pub updated_at: ::std::option::Option<::chrono::DateTime<::chrono::offset::Utc>>,
    }

    impl StatusResponse {
        pub fn builder() -> builder::StatusResponse {
            Default::default()
        }
    }

    ///algo (native, token id 0), asa (Algorand Standard Asset, also ARC-20
    /// Smart ASAs) or arc200 (application token).
    #[derive(
        :: serde :: Deserialize,
        :: serde :: Serialize,
        Clone,
        Copy,
        Debug,
        Eq,
        Hash,
        Ord,
        PartialEq,
        PartialOrd,
    )]
    pub enum TokenKind {
        #[serde(rename = "algo")]
        Algo,
        #[serde(rename = "asa")]
        Asa,
        #[serde(rename = "arc200")]
        Arc200,
    }

    impl ::std::fmt::Display for TokenKind {
        fn fmt(&self, f: &mut ::std::fmt::Formatter<'_>) -> ::std::fmt::Result {
            match *self {
                Self::Algo => f.write_str("algo"),
                Self::Asa => f.write_str("asa"),
                Self::Arc200 => f.write_str("arc200"),
            }
        }
    }

    impl ::std::str::FromStr for TokenKind {
        type Err = self::error::ConversionError;
        fn from_str(value: &str) -> ::std::result::Result<Self, self::error::ConversionError> {
            match value {
                "algo" => Ok(Self::Algo),
                "asa" => Ok(Self::Asa),
                "arc200" => Ok(Self::Arc200),
                _ => Err("invalid value".into()),
            }
        }
    }

    impl ::std::convert::TryFrom<&str> for TokenKind {
        type Error = self::error::ConversionError;
        fn try_from(value: &str) -> ::std::result::Result<Self, self::error::ConversionError> {
            value.parse()
        }
    }

    impl ::std::convert::TryFrom<::std::string::String> for TokenKind {
        type Error = self::error::ConversionError;
        fn try_from(
            value: ::std::string::String,
        ) -> ::std::result::Result<Self, self::error::ConversionError> {
            value.parse()
        }
    }

    ///`TokenRef`
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct TokenRef {
        pub kind: ::std::string::String,
        #[serde(rename = "tokenId")]
        pub token_id: u64,
    }

    impl TokenRef {
        pub fn builder() -> builder::TokenRef {
            Default::default()
        }
    }

    ///`TokenResponse`
    #[derive(:: serde :: Deserialize, :: serde :: Serialize, Clone, Debug)]
    pub struct TokenResponse {
        #[serde(
            rename = "controllingAppId",
            deserialize_with = "::std::option::Option::deserialize"
        )]
        pub controlling_app_id: ::std::option::Option<u64>,
        #[serde(
            rename = "createdRound",
            deserialize_with = "::std::option::Option::deserialize"
        )]
        pub created_round: ::std::option::Option<u64>,
        #[serde(deserialize_with = "::std::option::Option::deserialize")]
        pub creator: ::std::option::Option<::std::string::String>,
        #[serde(deserialize_with = "::std::option::Option::deserialize")]
        pub decimals: ::std::option::Option<i32>,
        #[serde(
            rename = "deletedRound",
            deserialize_with = "::std::option::Option::deserialize"
        )]
        pub deleted_round: ::std::option::Option<u64>,
        #[serde(deserialize_with = "::std::option::Option::deserialize")]
        pub name: ::std::option::Option<::std::string::String>,
        pub standard: ::std::string::String,
        #[serde(rename = "standardVerified")]
        pub standard_verified: bool,
        #[serde(deserialize_with = "::std::option::Option::deserialize")]
        pub symbol: ::std::option::Option<::std::string::String>,
        pub token: TokenRef,
    }

    impl TokenResponse {
        pub fn builder() -> builder::TokenResponse {
            Default::default()
        }
    }

    /// Types for composing complex structures.
    pub mod builder {
        #[derive(Clone, Debug)]
        pub struct BalanceResponse {
            account: ::std::result::Result<::std::string::String, ::std::string::String>,
            amount: ::std::result::Result<::std::string::String, ::std::string::String>,
            indexed_round: ::std::result::Result<u64, ::std::string::String>,
            round: ::std::result::Result<u64, ::std::string::String>,
            token: ::std::result::Result<super::TokenRef, ::std::string::String>,
        }

        impl ::std::default::Default for BalanceResponse {
            fn default() -> Self {
                Self {
                    account: Err("no value supplied for account".to_string()),
                    amount: Err("no value supplied for amount".to_string()),
                    indexed_round: Err("no value supplied for indexed_round".to_string()),
                    round: Err("no value supplied for round".to_string()),
                    token: Err("no value supplied for token".to_string()),
                }
            }
        }

        impl BalanceResponse {
            pub fn account<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.account = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for account: {e}"));
                self
            }
            pub fn amount<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.amount = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for amount: {e}"));
                self
            }
            pub fn indexed_round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<u64>,
                T::Error: ::std::fmt::Display,
            {
                self.indexed_round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for indexed_round: {e}"));
                self
            }
            pub fn round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<u64>,
                T::Error: ::std::fmt::Display,
            {
                self.round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for round: {e}"));
                self
            }
            pub fn token<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<super::TokenRef>,
                T::Error: ::std::fmt::Display,
            {
                self.token = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for token: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<BalanceResponse> for super::BalanceResponse {
            type Error = super::error::ConversionError;
            fn try_from(
                value: BalanceResponse,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    account: value.account?,
                    amount: value.amount?,
                    indexed_round: value.indexed_round?,
                    round: value.round?,
                    token: value.token?,
                })
            }
        }

        impl ::std::convert::From<super::BalanceResponse> for BalanceResponse {
            fn from(value: super::BalanceResponse) -> Self {
                Self {
                    account: Ok(value.account),
                    amount: Ok(value.amount),
                    indexed_round: Ok(value.indexed_round),
                    round: Ok(value.round),
                    token: Ok(value.token),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct EffectDto {
            balance_after: ::std::result::Result<::std::string::String, ::std::string::String>,
            counterparty: ::std::result::Result<
                ::std::option::Option<::std::string::String>,
                ::std::string::String,
            >,
            delta: ::std::result::Result<::std::string::String, ::std::string::String>,
            inner_path: ::std::result::Result<::std::vec::Vec<i32>, ::std::string::String>,
            intra_round_order: ::std::result::Result<i32, ::std::string::String>,
            ordinal: ::std::result::Result<i32, ::std::string::String>,
            round: ::std::result::Result<u64, ::std::string::String>,
            transaction_id: ::std::result::Result<::std::string::String, ::std::string::String>,
            type_: ::std::result::Result<::std::string::String, ::std::string::String>,
        }

        impl ::std::default::Default for EffectDto {
            fn default() -> Self {
                Self {
                    balance_after: Err("no value supplied for balance_after".to_string()),
                    counterparty: Err("no value supplied for counterparty".to_string()),
                    delta: Err("no value supplied for delta".to_string()),
                    inner_path: Err("no value supplied for inner_path".to_string()),
                    intra_round_order: Err("no value supplied for intra_round_order".to_string()),
                    ordinal: Err("no value supplied for ordinal".to_string()),
                    round: Err("no value supplied for round".to_string()),
                    transaction_id: Err("no value supplied for transaction_id".to_string()),
                    type_: Err("no value supplied for type_".to_string()),
                }
            }
        }

        impl EffectDto {
            pub fn balance_after<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.balance_after = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for balance_after: {e}"));
                self
            }
            pub fn counterparty<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<::std::string::String>>,
                T::Error: ::std::fmt::Display,
            {
                self.counterparty = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for counterparty: {e}"));
                self
            }
            pub fn delta<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.delta = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for delta: {e}"));
                self
            }
            pub fn inner_path<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::vec::Vec<i32>>,
                T::Error: ::std::fmt::Display,
            {
                self.inner_path = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for inner_path: {e}"));
                self
            }
            pub fn intra_round_order<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<i32>,
                T::Error: ::std::fmt::Display,
            {
                self.intra_round_order = value.try_into().map_err(|e| {
                    format!("error converting supplied value for intra_round_order: {e}")
                });
                self
            }
            pub fn ordinal<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<i32>,
                T::Error: ::std::fmt::Display,
            {
                self.ordinal = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for ordinal: {e}"));
                self
            }
            pub fn round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<u64>,
                T::Error: ::std::fmt::Display,
            {
                self.round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for round: {e}"));
                self
            }
            pub fn transaction_id<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.transaction_id = value.try_into().map_err(|e| {
                    format!("error converting supplied value for transaction_id: {e}")
                });
                self
            }
            pub fn type_<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.type_ = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for type_: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<EffectDto> for super::EffectDto {
            type Error = super::error::ConversionError;
            fn try_from(
                value: EffectDto,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    balance_after: value.balance_after?,
                    counterparty: value.counterparty?,
                    delta: value.delta?,
                    inner_path: value.inner_path?,
                    intra_round_order: value.intra_round_order?,
                    ordinal: value.ordinal?,
                    round: value.round?,
                    transaction_id: value.transaction_id?,
                    type_: value.type_?,
                })
            }
        }

        impl ::std::convert::From<super::EffectDto> for EffectDto {
            fn from(value: super::EffectDto) -> Self {
                Self {
                    balance_after: Ok(value.balance_after),
                    counterparty: Ok(value.counterparty),
                    delta: Ok(value.delta),
                    inner_path: Ok(value.inner_path),
                    intra_round_order: Ok(value.intra_round_order),
                    ordinal: Ok(value.ordinal),
                    round: Ok(value.round),
                    transaction_id: Ok(value.transaction_id),
                    type_: Ok(value.type_),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct ErrorResponse {
            error: ::std::result::Result<::std::string::String, ::std::string::String>,
        }

        impl ::std::default::Default for ErrorResponse {
            fn default() -> Self {
                Self {
                    error: Err("no value supplied for error".to_string()),
                }
            }
        }

        impl ErrorResponse {
            pub fn error<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.error = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for error: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<ErrorResponse> for super::ErrorResponse {
            type Error = super::error::ConversionError;
            fn try_from(
                value: ErrorResponse,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    error: value.error?,
                })
            }
        }

        impl ::std::convert::From<super::ErrorResponse> for ErrorResponse {
            fn from(value: super::ErrorResponse) -> Self {
                Self {
                    error: Ok(value.error),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct HistoryResponse {
            account: ::std::result::Result<::std::string::String, ::std::string::String>,
            indexed_round: ::std::result::Result<u64, ::std::string::String>,
            items: ::std::result::Result<::std::vec::Vec<super::EffectDto>, ::std::string::String>,
            next_cursor: ::std::result::Result<
                ::std::option::Option<::std::string::String>,
                ::std::string::String,
            >,
            token: ::std::result::Result<super::TokenRef, ::std::string::String>,
        }

        impl ::std::default::Default for HistoryResponse {
            fn default() -> Self {
                Self {
                    account: Err("no value supplied for account".to_string()),
                    indexed_round: Err("no value supplied for indexed_round".to_string()),
                    items: Err("no value supplied for items".to_string()),
                    next_cursor: Err("no value supplied for next_cursor".to_string()),
                    token: Err("no value supplied for token".to_string()),
                }
            }
        }

        impl HistoryResponse {
            pub fn account<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.account = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for account: {e}"));
                self
            }
            pub fn indexed_round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<u64>,
                T::Error: ::std::fmt::Display,
            {
                self.indexed_round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for indexed_round: {e}"));
                self
            }
            pub fn items<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::vec::Vec<super::EffectDto>>,
                T::Error: ::std::fmt::Display,
            {
                self.items = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for items: {e}"));
                self
            }
            pub fn next_cursor<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<::std::string::String>>,
                T::Error: ::std::fmt::Display,
            {
                self.next_cursor = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for next_cursor: {e}"));
                self
            }
            pub fn token<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<super::TokenRef>,
                T::Error: ::std::fmt::Display,
            {
                self.token = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for token: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<HistoryResponse> for super::HistoryResponse {
            type Error = super::error::ConversionError;
            fn try_from(
                value: HistoryResponse,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    account: value.account?,
                    indexed_round: value.indexed_round?,
                    items: value.items?,
                    next_cursor: value.next_cursor?,
                    token: value.token?,
                })
            }
        }

        impl ::std::convert::From<super::HistoryResponse> for HistoryResponse {
            fn from(value: super::HistoryResponse) -> Self {
                Self {
                    account: Ok(value.account),
                    indexed_round: Ok(value.indexed_round),
                    items: Ok(value.items),
                    next_cursor: Ok(value.next_cursor),
                    token: Ok(value.token),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct HolderDto {
            account: ::std::result::Result<::std::string::String, ::std::string::String>,
            amount: ::std::result::Result<::std::string::String, ::std::string::String>,
            last_changed_round: ::std::result::Result<u64, ::std::string::String>,
            opted_in: ::std::result::Result<bool, ::std::string::String>,
        }

        impl ::std::default::Default for HolderDto {
            fn default() -> Self {
                Self {
                    account: Err("no value supplied for account".to_string()),
                    amount: Err("no value supplied for amount".to_string()),
                    last_changed_round: Err("no value supplied for last_changed_round".to_string()),
                    opted_in: Err("no value supplied for opted_in".to_string()),
                }
            }
        }

        impl HolderDto {
            pub fn account<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.account = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for account: {e}"));
                self
            }
            pub fn amount<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.amount = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for amount: {e}"));
                self
            }
            pub fn last_changed_round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<u64>,
                T::Error: ::std::fmt::Display,
            {
                self.last_changed_round = value.try_into().map_err(|e| {
                    format!("error converting supplied value for last_changed_round: {e}")
                });
                self
            }
            pub fn opted_in<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<bool>,
                T::Error: ::std::fmt::Display,
            {
                self.opted_in = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for opted_in: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<HolderDto> for super::HolderDto {
            type Error = super::error::ConversionError;
            fn try_from(
                value: HolderDto,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    account: value.account?,
                    amount: value.amount?,
                    last_changed_round: value.last_changed_round?,
                    opted_in: value.opted_in?,
                })
            }
        }

        impl ::std::convert::From<super::HolderDto> for HolderDto {
            fn from(value: super::HolderDto) -> Self {
                Self {
                    account: Ok(value.account),
                    amount: Ok(value.amount),
                    last_changed_round: Ok(value.last_changed_round),
                    opted_in: Ok(value.opted_in),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct HoldersResponse {
            indexed_round: ::std::result::Result<u64, ::std::string::String>,
            items: ::std::result::Result<::std::vec::Vec<super::HolderDto>, ::std::string::String>,
            next_cursor: ::std::result::Result<
                ::std::option::Option<::std::string::String>,
                ::std::string::String,
            >,
            round: ::std::result::Result<u64, ::std::string::String>,
            token: ::std::result::Result<super::TokenRef, ::std::string::String>,
        }

        impl ::std::default::Default for HoldersResponse {
            fn default() -> Self {
                Self {
                    indexed_round: Err("no value supplied for indexed_round".to_string()),
                    items: Err("no value supplied for items".to_string()),
                    next_cursor: Err("no value supplied for next_cursor".to_string()),
                    round: Err("no value supplied for round".to_string()),
                    token: Err("no value supplied for token".to_string()),
                }
            }
        }

        impl HoldersResponse {
            pub fn indexed_round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<u64>,
                T::Error: ::std::fmt::Display,
            {
                self.indexed_round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for indexed_round: {e}"));
                self
            }
            pub fn items<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::vec::Vec<super::HolderDto>>,
                T::Error: ::std::fmt::Display,
            {
                self.items = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for items: {e}"));
                self
            }
            pub fn next_cursor<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<::std::string::String>>,
                T::Error: ::std::fmt::Display,
            {
                self.next_cursor = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for next_cursor: {e}"));
                self
            }
            pub fn round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<u64>,
                T::Error: ::std::fmt::Display,
            {
                self.round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for round: {e}"));
                self
            }
            pub fn token<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<super::TokenRef>,
                T::Error: ::std::fmt::Display,
            {
                self.token = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for token: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<HoldersResponse> for super::HoldersResponse {
            type Error = super::error::ConversionError;
            fn try_from(
                value: HoldersResponse,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    indexed_round: value.indexed_round?,
                    items: value.items?,
                    next_cursor: value.next_cursor?,
                    round: value.round?,
                    token: value.token?,
                })
            }
        }

        impl ::std::convert::From<super::HoldersResponse> for HoldersResponse {
            fn from(value: super::HoldersResponse) -> Self {
                Self {
                    indexed_round: Ok(value.indexed_round),
                    items: Ok(value.items),
                    next_cursor: Ok(value.next_cursor),
                    round: Ok(value.round),
                    token: Ok(value.token),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct HoldingDto {
            amount: ::std::result::Result<::std::string::String, ::std::string::String>,
            last_changed_round: ::std::result::Result<u64, ::std::string::String>,
            opted_in: ::std::result::Result<bool, ::std::string::String>,
            token: ::std::result::Result<super::TokenRef, ::std::string::String>,
        }

        impl ::std::default::Default for HoldingDto {
            fn default() -> Self {
                Self {
                    amount: Err("no value supplied for amount".to_string()),
                    last_changed_round: Err("no value supplied for last_changed_round".to_string()),
                    opted_in: Err("no value supplied for opted_in".to_string()),
                    token: Err("no value supplied for token".to_string()),
                }
            }
        }

        impl HoldingDto {
            pub fn amount<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.amount = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for amount: {e}"));
                self
            }
            pub fn last_changed_round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<u64>,
                T::Error: ::std::fmt::Display,
            {
                self.last_changed_round = value.try_into().map_err(|e| {
                    format!("error converting supplied value for last_changed_round: {e}")
                });
                self
            }
            pub fn opted_in<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<bool>,
                T::Error: ::std::fmt::Display,
            {
                self.opted_in = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for opted_in: {e}"));
                self
            }
            pub fn token<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<super::TokenRef>,
                T::Error: ::std::fmt::Display,
            {
                self.token = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for token: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<HoldingDto> for super::HoldingDto {
            type Error = super::error::ConversionError;
            fn try_from(
                value: HoldingDto,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    amount: value.amount?,
                    last_changed_round: value.last_changed_round?,
                    opted_in: value.opted_in?,
                    token: value.token?,
                })
            }
        }

        impl ::std::convert::From<super::HoldingDto> for HoldingDto {
            fn from(value: super::HoldingDto) -> Self {
                Self {
                    amount: Ok(value.amount),
                    last_changed_round: Ok(value.last_changed_round),
                    opted_in: Ok(value.opted_in),
                    token: Ok(value.token),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct HoldingsResponse {
            account: ::std::result::Result<::std::string::String, ::std::string::String>,
            indexed_round: ::std::result::Result<u64, ::std::string::String>,
            items: ::std::result::Result<::std::vec::Vec<super::HoldingDto>, ::std::string::String>,
            next_cursor: ::std::result::Result<
                ::std::option::Option<::std::string::String>,
                ::std::string::String,
            >,
        }

        impl ::std::default::Default for HoldingsResponse {
            fn default() -> Self {
                Self {
                    account: Err("no value supplied for account".to_string()),
                    indexed_round: Err("no value supplied for indexed_round".to_string()),
                    items: Err("no value supplied for items".to_string()),
                    next_cursor: Err("no value supplied for next_cursor".to_string()),
                }
            }
        }

        impl HoldingsResponse {
            pub fn account<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.account = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for account: {e}"));
                self
            }
            pub fn indexed_round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<u64>,
                T::Error: ::std::fmt::Display,
            {
                self.indexed_round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for indexed_round: {e}"));
                self
            }
            pub fn items<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::vec::Vec<super::HoldingDto>>,
                T::Error: ::std::fmt::Display,
            {
                self.items = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for items: {e}"));
                self
            }
            pub fn next_cursor<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<::std::string::String>>,
                T::Error: ::std::fmt::Display,
            {
                self.next_cursor = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for next_cursor: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<HoldingsResponse> for super::HoldingsResponse {
            type Error = super::error::ConversionError;
            fn try_from(
                value: HoldingsResponse,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    account: value.account?,
                    indexed_round: value.indexed_round?,
                    items: value.items?,
                    next_cursor: value.next_cursor?,
                })
            }
        }

        impl ::std::convert::From<super::HoldingsResponse> for HoldingsResponse {
            fn from(value: super::HoldingsResponse) -> Self {
                Self {
                    account: Ok(value.account),
                    indexed_round: Ok(value.indexed_round),
                    items: Ok(value.items),
                    next_cursor: Ok(value.next_cursor),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct ScanHoldersResponse {
            complete: ::std::result::Result<bool, ::std::string::String>,
            id: ::std::result::Result<::uuid::Uuid, ::std::string::String>,
            items: ::std::result::Result<::std::vec::Vec<super::HolderDto>, ::std::string::String>,
            next_cursor: ::std::result::Result<
                ::std::option::Option<::std::string::String>,
                ::std::string::String,
            >,
            round: ::std::result::Result<u64, ::std::string::String>,
            state: ::std::result::Result<::std::string::String, ::std::string::String>,
        }

        impl ::std::default::Default for ScanHoldersResponse {
            fn default() -> Self {
                Self {
                    complete: Err("no value supplied for complete".to_string()),
                    id: Err("no value supplied for id".to_string()),
                    items: Err("no value supplied for items".to_string()),
                    next_cursor: Err("no value supplied for next_cursor".to_string()),
                    round: Err("no value supplied for round".to_string()),
                    state: Err("no value supplied for state".to_string()),
                }
            }
        }

        impl ScanHoldersResponse {
            pub fn complete<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<bool>,
                T::Error: ::std::fmt::Display,
            {
                self.complete = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for complete: {e}"));
                self
            }
            pub fn id<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::uuid::Uuid>,
                T::Error: ::std::fmt::Display,
            {
                self.id = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for id: {e}"));
                self
            }
            pub fn items<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::vec::Vec<super::HolderDto>>,
                T::Error: ::std::fmt::Display,
            {
                self.items = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for items: {e}"));
                self
            }
            pub fn next_cursor<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<::std::string::String>>,
                T::Error: ::std::fmt::Display,
            {
                self.next_cursor = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for next_cursor: {e}"));
                self
            }
            pub fn round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<u64>,
                T::Error: ::std::fmt::Display,
            {
                self.round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for round: {e}"));
                self
            }
            pub fn state<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.state = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for state: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<ScanHoldersResponse> for super::ScanHoldersResponse {
            type Error = super::error::ConversionError;
            fn try_from(
                value: ScanHoldersResponse,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    complete: value.complete?,
                    id: value.id?,
                    items: value.items?,
                    next_cursor: value.next_cursor?,
                    round: value.round?,
                    state: value.state?,
                })
            }
        }

        impl ::std::convert::From<super::ScanHoldersResponse> for ScanHoldersResponse {
            fn from(value: super::ScanHoldersResponse) -> Self {
                Self {
                    complete: Ok(value.complete),
                    id: Ok(value.id),
                    items: Ok(value.items),
                    next_cursor: Ok(value.next_cursor),
                    round: Ok(value.round),
                    state: Ok(value.state),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct ScanRequest {
            include_zero: ::std::result::Result<::std::option::Option<bool>, ::std::string::String>,
            round: ::std::result::Result<::std::option::Option<u64>, ::std::string::String>,
        }

        impl ::std::default::Default for ScanRequest {
            fn default() -> Self {
                Self {
                    include_zero: Ok(Default::default()),
                    round: Ok(Default::default()),
                }
            }
        }

        impl ScanRequest {
            pub fn include_zero<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<bool>>,
                T::Error: ::std::fmt::Display,
            {
                self.include_zero = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for include_zero: {e}"));
                self
            }
            pub fn round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<u64>>,
                T::Error: ::std::fmt::Display,
            {
                self.round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for round: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<ScanRequest> for super::ScanRequest {
            type Error = super::error::ConversionError;
            fn try_from(
                value: ScanRequest,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    include_zero: value.include_zero?,
                    round: value.round?,
                })
            }
        }

        impl ::std::convert::From<super::ScanRequest> for ScanRequest {
            fn from(value: super::ScanRequest) -> Self {
                Self {
                    include_zero: Ok(value.include_zero),
                    round: Ok(value.round),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct ScanResponse {
            complete: ::std::result::Result<bool, ::std::string::String>,
            created_at: ::std::result::Result<
                ::chrono::DateTime<::chrono::offset::Utc>,
                ::std::string::String,
            >,
            error: ::std::result::Result<
                ::std::option::Option<::std::string::String>,
                ::std::string::String,
            >,
            expires_at: ::std::result::Result<
                ::chrono::DateTime<::chrono::offset::Utc>,
                ::std::string::String,
            >,
            holders_found: ::std::result::Result<i64, ::std::string::String>,
            id: ::std::result::Result<::uuid::Uuid, ::std::string::String>,
            include_zero: ::std::result::Result<bool, ::std::string::String>,
            round: ::std::result::Result<u64, ::std::string::String>,
            state: ::std::result::Result<::std::string::String, ::std::string::String>,
            token: ::std::result::Result<super::TokenRef, ::std::string::String>,
            updated_at: ::std::result::Result<
                ::chrono::DateTime<::chrono::offset::Utc>,
                ::std::string::String,
            >,
        }

        impl ::std::default::Default for ScanResponse {
            fn default() -> Self {
                Self {
                    complete: Err("no value supplied for complete".to_string()),
                    created_at: Err("no value supplied for created_at".to_string()),
                    error: Err("no value supplied for error".to_string()),
                    expires_at: Err("no value supplied for expires_at".to_string()),
                    holders_found: Err("no value supplied for holders_found".to_string()),
                    id: Err("no value supplied for id".to_string()),
                    include_zero: Err("no value supplied for include_zero".to_string()),
                    round: Err("no value supplied for round".to_string()),
                    state: Err("no value supplied for state".to_string()),
                    token: Err("no value supplied for token".to_string()),
                    updated_at: Err("no value supplied for updated_at".to_string()),
                }
            }
        }

        impl ScanResponse {
            pub fn complete<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<bool>,
                T::Error: ::std::fmt::Display,
            {
                self.complete = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for complete: {e}"));
                self
            }
            pub fn created_at<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::chrono::DateTime<::chrono::offset::Utc>>,
                T::Error: ::std::fmt::Display,
            {
                self.created_at = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for created_at: {e}"));
                self
            }
            pub fn error<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<::std::string::String>>,
                T::Error: ::std::fmt::Display,
            {
                self.error = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for error: {e}"));
                self
            }
            pub fn expires_at<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::chrono::DateTime<::chrono::offset::Utc>>,
                T::Error: ::std::fmt::Display,
            {
                self.expires_at = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for expires_at: {e}"));
                self
            }
            pub fn holders_found<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<i64>,
                T::Error: ::std::fmt::Display,
            {
                self.holders_found = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for holders_found: {e}"));
                self
            }
            pub fn id<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::uuid::Uuid>,
                T::Error: ::std::fmt::Display,
            {
                self.id = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for id: {e}"));
                self
            }
            pub fn include_zero<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<bool>,
                T::Error: ::std::fmt::Display,
            {
                self.include_zero = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for include_zero: {e}"));
                self
            }
            pub fn round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<u64>,
                T::Error: ::std::fmt::Display,
            {
                self.round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for round: {e}"));
                self
            }
            pub fn state<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.state = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for state: {e}"));
                self
            }
            pub fn token<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<super::TokenRef>,
                T::Error: ::std::fmt::Display,
            {
                self.token = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for token: {e}"));
                self
            }
            pub fn updated_at<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::chrono::DateTime<::chrono::offset::Utc>>,
                T::Error: ::std::fmt::Display,
            {
                self.updated_at = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for updated_at: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<ScanResponse> for super::ScanResponse {
            type Error = super::error::ConversionError;
            fn try_from(
                value: ScanResponse,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    complete: value.complete?,
                    created_at: value.created_at?,
                    error: value.error?,
                    expires_at: value.expires_at?,
                    holders_found: value.holders_found?,
                    id: value.id?,
                    include_zero: value.include_zero?,
                    round: value.round?,
                    state: value.state?,
                    token: value.token?,
                    updated_at: value.updated_at?,
                })
            }
        }

        impl ::std::convert::From<super::ScanResponse> for ScanResponse {
            fn from(value: super::ScanResponse) -> Self {
                Self {
                    complete: Ok(value.complete),
                    created_at: Ok(value.created_at),
                    error: Ok(value.error),
                    expires_at: Ok(value.expires_at),
                    holders_found: Ok(value.holders_found),
                    id: Ok(value.id),
                    include_zero: Ok(value.include_zero),
                    round: Ok(value.round),
                    state: Ok(value.state),
                    token: Ok(value.token),
                    updated_at: Ok(value.updated_at),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct StatusResponse {
            chain: ::std::result::Result<::std::string::String, ::std::string::String>,
            indexed_round: ::std::result::Result<::std::option::Option<u64>, ::std::string::String>,
            lag: ::std::result::Result<::std::option::Option<u64>, ::std::string::String>,
            provider: ::std::result::Result<::std::string::String, ::std::string::String>,
            state: ::std::result::Result<::std::string::String, ::std::string::String>,
            target_round: ::std::result::Result<::std::option::Option<u64>, ::std::string::String>,
            updated_at: ::std::result::Result<
                ::std::option::Option<::chrono::DateTime<::chrono::offset::Utc>>,
                ::std::string::String,
            >,
        }

        impl ::std::default::Default for StatusResponse {
            fn default() -> Self {
                Self {
                    chain: Err("no value supplied for chain".to_string()),
                    indexed_round: Err("no value supplied for indexed_round".to_string()),
                    lag: Err("no value supplied for lag".to_string()),
                    provider: Err("no value supplied for provider".to_string()),
                    state: Err("no value supplied for state".to_string()),
                    target_round: Err("no value supplied for target_round".to_string()),
                    updated_at: Err("no value supplied for updated_at".to_string()),
                }
            }
        }

        impl StatusResponse {
            pub fn chain<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.chain = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for chain: {e}"));
                self
            }
            pub fn indexed_round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<u64>>,
                T::Error: ::std::fmt::Display,
            {
                self.indexed_round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for indexed_round: {e}"));
                self
            }
            pub fn lag<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<u64>>,
                T::Error: ::std::fmt::Display,
            {
                self.lag = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for lag: {e}"));
                self
            }
            pub fn provider<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.provider = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for provider: {e}"));
                self
            }
            pub fn state<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.state = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for state: {e}"));
                self
            }
            pub fn target_round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<u64>>,
                T::Error: ::std::fmt::Display,
            {
                self.target_round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for target_round: {e}"));
                self
            }
            pub fn updated_at<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<
                    ::std::option::Option<::chrono::DateTime<::chrono::offset::Utc>>,
                >,
                T::Error: ::std::fmt::Display,
            {
                self.updated_at = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for updated_at: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<StatusResponse> for super::StatusResponse {
            type Error = super::error::ConversionError;
            fn try_from(
                value: StatusResponse,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    chain: value.chain?,
                    indexed_round: value.indexed_round?,
                    lag: value.lag?,
                    provider: value.provider?,
                    state: value.state?,
                    target_round: value.target_round?,
                    updated_at: value.updated_at?,
                })
            }
        }

        impl ::std::convert::From<super::StatusResponse> for StatusResponse {
            fn from(value: super::StatusResponse) -> Self {
                Self {
                    chain: Ok(value.chain),
                    indexed_round: Ok(value.indexed_round),
                    lag: Ok(value.lag),
                    provider: Ok(value.provider),
                    state: Ok(value.state),
                    target_round: Ok(value.target_round),
                    updated_at: Ok(value.updated_at),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct TokenRef {
            kind: ::std::result::Result<::std::string::String, ::std::string::String>,
            token_id: ::std::result::Result<u64, ::std::string::String>,
        }

        impl ::std::default::Default for TokenRef {
            fn default() -> Self {
                Self {
                    kind: Err("no value supplied for kind".to_string()),
                    token_id: Err("no value supplied for token_id".to_string()),
                }
            }
        }

        impl TokenRef {
            pub fn kind<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.kind = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for kind: {e}"));
                self
            }
            pub fn token_id<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<u64>,
                T::Error: ::std::fmt::Display,
            {
                self.token_id = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for token_id: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<TokenRef> for super::TokenRef {
            type Error = super::error::ConversionError;
            fn try_from(
                value: TokenRef,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    kind: value.kind?,
                    token_id: value.token_id?,
                })
            }
        }

        impl ::std::convert::From<super::TokenRef> for TokenRef {
            fn from(value: super::TokenRef) -> Self {
                Self {
                    kind: Ok(value.kind),
                    token_id: Ok(value.token_id),
                }
            }
        }

        #[derive(Clone, Debug)]
        pub struct TokenResponse {
            controlling_app_id:
                ::std::result::Result<::std::option::Option<u64>, ::std::string::String>,
            created_round: ::std::result::Result<::std::option::Option<u64>, ::std::string::String>,
            creator: ::std::result::Result<
                ::std::option::Option<::std::string::String>,
                ::std::string::String,
            >,
            decimals: ::std::result::Result<::std::option::Option<i32>, ::std::string::String>,
            deleted_round: ::std::result::Result<::std::option::Option<u64>, ::std::string::String>,
            name: ::std::result::Result<
                ::std::option::Option<::std::string::String>,
                ::std::string::String,
            >,
            standard: ::std::result::Result<::std::string::String, ::std::string::String>,
            standard_verified: ::std::result::Result<bool, ::std::string::String>,
            symbol: ::std::result::Result<
                ::std::option::Option<::std::string::String>,
                ::std::string::String,
            >,
            token: ::std::result::Result<super::TokenRef, ::std::string::String>,
        }

        impl ::std::default::Default for TokenResponse {
            fn default() -> Self {
                Self {
                    controlling_app_id: Err("no value supplied for controlling_app_id".to_string()),
                    created_round: Err("no value supplied for created_round".to_string()),
                    creator: Err("no value supplied for creator".to_string()),
                    decimals: Err("no value supplied for decimals".to_string()),
                    deleted_round: Err("no value supplied for deleted_round".to_string()),
                    name: Err("no value supplied for name".to_string()),
                    standard: Err("no value supplied for standard".to_string()),
                    standard_verified: Err("no value supplied for standard_verified".to_string()),
                    symbol: Err("no value supplied for symbol".to_string()),
                    token: Err("no value supplied for token".to_string()),
                }
            }
        }

        impl TokenResponse {
            pub fn controlling_app_id<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<u64>>,
                T::Error: ::std::fmt::Display,
            {
                self.controlling_app_id = value.try_into().map_err(|e| {
                    format!("error converting supplied value for controlling_app_id: {e}")
                });
                self
            }
            pub fn created_round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<u64>>,
                T::Error: ::std::fmt::Display,
            {
                self.created_round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for created_round: {e}"));
                self
            }
            pub fn creator<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<::std::string::String>>,
                T::Error: ::std::fmt::Display,
            {
                self.creator = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for creator: {e}"));
                self
            }
            pub fn decimals<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<i32>>,
                T::Error: ::std::fmt::Display,
            {
                self.decimals = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for decimals: {e}"));
                self
            }
            pub fn deleted_round<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<u64>>,
                T::Error: ::std::fmt::Display,
            {
                self.deleted_round = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for deleted_round: {e}"));
                self
            }
            pub fn name<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<::std::string::String>>,
                T::Error: ::std::fmt::Display,
            {
                self.name = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for name: {e}"));
                self
            }
            pub fn standard<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::string::String>,
                T::Error: ::std::fmt::Display,
            {
                self.standard = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for standard: {e}"));
                self
            }
            pub fn standard_verified<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<bool>,
                T::Error: ::std::fmt::Display,
            {
                self.standard_verified = value.try_into().map_err(|e| {
                    format!("error converting supplied value for standard_verified: {e}")
                });
                self
            }
            pub fn symbol<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<::std::option::Option<::std::string::String>>,
                T::Error: ::std::fmt::Display,
            {
                self.symbol = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for symbol: {e}"));
                self
            }
            pub fn token<T>(mut self, value: T) -> Self
            where
                T: ::std::convert::TryInto<super::TokenRef>,
                T::Error: ::std::fmt::Display,
            {
                self.token = value
                    .try_into()
                    .map_err(|e| format!("error converting supplied value for token: {e}"));
                self
            }
        }

        impl ::std::convert::TryFrom<TokenResponse> for super::TokenResponse {
            type Error = super::error::ConversionError;
            fn try_from(
                value: TokenResponse,
            ) -> ::std::result::Result<Self, super::error::ConversionError> {
                Ok(Self {
                    controlling_app_id: value.controlling_app_id?,
                    created_round: value.created_round?,
                    creator: value.creator?,
                    decimals: value.decimals?,
                    deleted_round: value.deleted_round?,
                    name: value.name?,
                    standard: value.standard?,
                    standard_verified: value.standard_verified?,
                    symbol: value.symbol?,
                    token: value.token?,
                })
            }
        }

        impl ::std::convert::From<super::TokenResponse> for TokenResponse {
            fn from(value: super::TokenResponse) -> Self {
                Self {
                    controlling_app_id: Ok(value.controlling_app_id),
                    created_round: Ok(value.created_round),
                    creator: Ok(value.creator),
                    decimals: Ok(value.decimals),
                    deleted_round: Ok(value.deleted_round),
                    name: Ok(value.name),
                    standard: Ok(value.standard),
                    standard_verified: Ok(value.standard_verified),
                    symbol: Ok(value.symbol),
                    token: Ok(value.token),
                }
            }
        }
    }

    /// Error types.
    pub mod error {
        /// Error from a `TryFrom` or `FromStr` implementation.
        pub struct ConversionError(::std::borrow::Cow<'static, str>);
        impl ::std::error::Error for ConversionError {}
        impl ::std::fmt::Display for ConversionError {
            fn fmt(&self, f: &mut ::std::fmt::Formatter<'_>) -> Result<(), ::std::fmt::Error> {
                ::std::fmt::Display::fmt(&self.0, f)
            }
        }

        impl ::std::fmt::Debug for ConversionError {
            fn fmt(&self, f: &mut ::std::fmt::Formatter<'_>) -> Result<(), ::std::fmt::Error> {
                ::std::fmt::Debug::fmt(&self.0, f)
            }
        }

        impl From<&'static str> for ConversionError {
            fn from(value: &'static str) -> Self {
                Self(value.into())
            }
        }

        impl From<String> for ConversionError {
            fn from(value: String) -> Self {
                Self(value.into())
            }
        }
    }
}

#[derive(Clone, Debug)]
///Client for AVM Balance Indexer API
///
///Token balances, holders and history for Algorand-family chains (ALGO, ASA,
/// ARC-20, ARC-200). Every deployment serves one chain under /v1/{chain}.
/// Amounts and deltas are decimal strings; rounds are integers.
///
///Version: v1
pub struct Client {
    pub(crate) baseurl: String,
    pub(crate) client: reqwest::Client,
}

impl Client {
    /// Create a new client.
    ///
    /// `baseurl` is the base URL provided to the internal
    /// `reqwest::Client`, and should include a scheme and hostname,
    /// as well as port and a path stem if applicable.
    pub fn new(baseurl: &str) -> Self {
        #[cfg(not(target_arch = "wasm32"))]
        let client = {
            let dur = ::std::time::Duration::from_secs(15u64);
            reqwest::ClientBuilder::new()
                .connect_timeout(dur)
                .timeout(dur)
        };
        #[cfg(target_arch = "wasm32")]
        let client = reqwest::ClientBuilder::new();
        Self::new_with_client(baseurl, client.build().unwrap())
    }

    /// Construct a new client with an existing `reqwest::Client`,
    /// allowing more control over its configuration.
    ///
    /// `baseurl` is the base URL provided to the internal
    /// `reqwest::Client`, and should include a scheme and hostname,
    /// as well as port and a path stem if applicable.
    pub fn new_with_client(baseurl: &str, client: reqwest::Client) -> Self {
        Self {
            baseurl: baseurl.to_string(),
            client,
        }
    }
}

impl ClientInfo<()> for Client {
    fn api_version() -> &'static str {
        "v1"
    }

    fn baseurl(&self) -> &str {
        self.baseurl.as_str()
    }

    fn client(&self) -> &reqwest::Client {
        &self.client
    }

    fn inner(&self) -> &() {
        &()
    }
}

impl ClientHooks<()> for &Client {}
impl Client {
    ///Indexed round, target round, lag and ingestion state
    ///
    ///Sends a `GET` request to `/v1/{chain}/status`
    ///
    ///Arguments:
    /// - `chain`: The chain this deployment serves.
    ///```ignore
    /// let response = client.get_chain_status()
    ///    .chain(chain)
    ///    .send()
    ///    .await;
    /// ```
    pub fn get_chain_status(&self) -> builder::GetChainStatus<'_> {
        builder::GetChainStatus::new(self)
    }

    ///Current token holdings of an account
    ///
    ///Holdings ordered by token kind and id. Zero balances (opt-ins) are
    /// listed with includeZero=true. Page with nextCursor.
    ///
    ///Sends a `GET` request to `/v1/{chain}/accounts/{address}/tokens`
    ///
    ///Arguments:
    /// - `chain`: The chain this deployment serves.
    /// - `address`
    /// - `cursor`: Opaque cursor from the previous page's nextCursor.
    /// - `include_zero`: Also list accounts that are opted in but hold zero.
    /// - `limit`: Page size, 1 to 1000 (default 100).
    ///```ignore
    /// let response = client.get_account_tokens()
    ///    .chain(chain)
    ///    .address(address)
    ///    .cursor(cursor)
    ///    .include_zero(include_zero)
    ///    .limit(limit)
    ///    .send()
    ///    .await;
    /// ```
    pub fn get_account_tokens(&self) -> builder::GetAccountTokens<'_> {
        builder::GetAccountTokens::new(self)
    }

    ///End-of-round balance of an account
    ///
    ///The balance after the last effect at or before the round (default: the
    /// latest published round), including rounds without activity. An explicit
    /// published round is cacheable.
    ///
    ///Sends a `GET` request to
    /// `/v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/balance`
    ///
    ///Arguments:
    /// - `chain`: The chain this deployment serves.
    /// - `address`
    /// - `kind`: Token kind: algo (native), asa (Algorand Standard Asset, also
    ///   ARC-20) or arc200 (application token).
    /// - `token_id`: Asset id, application id, or 0 for the native token.
    /// - `round`: Chain round (end-of-round state). Omit for the latest
    ///   published round.
    ///```ignore
    /// let response = client.get_token_balance()
    ///    .chain(chain)
    ///    .address(address)
    ///    .kind(kind)
    ///    .token_id(token_id)
    ///    .round(round)
    ///    .send()
    ///    .await;
    /// ```
    pub fn get_token_balance(&self) -> builder::GetTokenBalance<'_> {
        builder::GetTokenBalance::new(self)
    }

    ///Balance effects of an account and token, newest first
    ///
    ///Every change of the balance with its signed delta, the balance after it
    /// and the transaction it came from. Limit the range with fromRound and
    /// toRound.
    ///
    ///Sends a `GET` request to
    /// `/v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/transactions`
    ///
    ///Arguments:
    /// - `chain`: The chain this deployment serves.
    /// - `address`
    /// - `kind`: Token kind: algo (native), asa (Algorand Standard Asset, also
    ///   ARC-20) or arc200 (application token).
    /// - `token_id`: Asset id, application id, or 0 for the native token.
    /// - `cursor`: Opaque cursor from the previous page's nextCursor.
    /// - `from_round`: Oldest round to include. Omit for no lower bound.
    /// - `limit`: Page size, 1 to 1000 (default 100).
    /// - `to_round`: Newest round to include. Omit for no upper bound.
    ///```ignore
    /// let response = client.get_token_history()
    ///    .chain(chain)
    ///    .address(address)
    ///    .kind(kind)
    ///    .token_id(token_id)
    ///    .cursor(cursor)
    ///    .from_round(from_round)
    ///    .limit(limit)
    ///    .to_round(to_round)
    ///    .send()
    ///    .await;
    /// ```
    pub fn get_token_history(&self) -> builder::GetTokenHistory<'_> {
        builder::GetTokenHistory::new(self)
    }

    ///Holders of a token at the end of a round
    ///
    ///Holders in account order. For a very large token at a past round use an
    /// asynchronous scan. Pass the same round while paging.
    ///
    ///Sends a `GET` request to `/v1/{chain}/tokens/{kind}/{tokenId}/holders`
    ///
    ///Arguments:
    /// - `chain`: The chain this deployment serves.
    /// - `kind`: Token kind: algo (native), asa (Algorand Standard Asset, also
    ///   ARC-20) or arc200 (application token).
    /// - `token_id`: Asset id, application id, or 0 for the native token.
    /// - `cursor`: Opaque cursor from the previous page's nextCursor.
    /// - `include_zero`: Also list accounts that are opted in but hold zero.
    /// - `limit`: Page size, 1 to 1000 (default 100).
    /// - `round`: Chain round (end-of-round state). Omit for the latest
    ///   published round.
    ///```ignore
    /// let response = client.get_token_holders()
    ///    .chain(chain)
    ///    .kind(kind)
    ///    .token_id(token_id)
    ///    .cursor(cursor)
    ///    .include_zero(include_zero)
    ///    .limit(limit)
    ///    .round(round)
    ///    .send()
    ///    .await;
    /// ```
    pub fn get_token_holders(&self) -> builder::GetTokenHolders<'_> {
        builder::GetTokenHolders::new(self)
    }

    ///Token metadata
    ///
    ///Sends a `GET` request to `/v1/{chain}/tokens/{kind}/{tokenId}`
    ///
    ///Arguments:
    /// - `chain`: The chain this deployment serves.
    /// - `kind`: Token kind: algo (native), asa (Algorand Standard Asset, also
    ///   ARC-20) or arc200 (application token).
    /// - `token_id`: Asset id, application id, or 0 for the native token.
    ///```ignore
    /// let response = client.get_token()
    ///    .chain(chain)
    ///    .kind(kind)
    ///    .token_id(token_id)
    ///    .send()
    ///    .await;
    /// ```
    pub fn get_token(&self) -> builder::GetToken<'_> {
        builder::GetToken::new(self)
    }

    ///Start an asynchronous export of every holder of a token at a round
    ///
    ///Accepted at once; poll the scan and read the result pages. The body is
    /// optional: { "round": 123, "includeZero": false }.
    ///
    ///Sends a `POST` request to
    /// `/v1/{chain}/tokens/{kind}/{tokenId}/holders/scans`
    ///
    ///Arguments:
    /// - `chain`: The chain this deployment serves.
    /// - `kind`: Token kind: algo (native), asa (Algorand Standard Asset, also
    ///   ARC-20) or arc200 (application token).
    /// - `token_id`: Asset id, application id, or 0 for the native token.
    /// - `body`
    ///```ignore
    /// let response = client.create_holder_scan()
    ///    .chain(chain)
    ///    .kind(kind)
    ///    .token_id(token_id)
    ///    .body(body)
    ///    .send()
    ///    .await;
    /// ```
    pub fn create_holder_scan(&self) -> builder::CreateHolderScan<'_> {
        builder::CreateHolderScan::new(self)
    }

    ///State and progress of a scan
    ///
    ///Sends a `GET` request to `/v1/{chain}/scans/{id}`
    ///
    ///Arguments:
    /// - `chain`: The chain this deployment serves.
    /// - `id`
    ///```ignore
    /// let response = client.get_holder_scan()
    ///    .chain(chain)
    ///    .id(id)
    ///    .send()
    ///    .await;
    /// ```
    pub fn get_holder_scan(&self) -> builder::GetHolderScan<'_> {
        builder::GetHolderScan::new(self)
    }

    ///Cancel a scan
    ///
    ///Sends a `DELETE` request to `/v1/{chain}/scans/{id}`
    ///
    ///Arguments:
    /// - `chain`: The chain this deployment serves.
    /// - `id`
    ///```ignore
    /// let response = client.cancel_holder_scan()
    ///    .chain(chain)
    ///    .id(id)
    ///    .send()
    ///    .await;
    /// ```
    pub fn cancel_holder_scan(&self) -> builder::CancelHolderScan<'_> {
        builder::CancelHolderScan::new(self)
    }

    ///Result pages of a scan (also while it is running)
    ///
    ///Without a nextCursor and with complete=false, repeat the same request a
    /// moment later.
    ///
    ///Sends a `GET` request to `/v1/{chain}/scans/{id}/holders`
    ///
    ///Arguments:
    /// - `chain`: The chain this deployment serves.
    /// - `id`
    /// - `cursor`: Opaque cursor from the previous page's nextCursor.
    /// - `limit`: Page size, 1 to 1000 (default 100).
    ///```ignore
    /// let response = client.get_holder_scan_result()
    ///    .chain(chain)
    ///    .id(id)
    ///    .cursor(cursor)
    ///    .limit(limit)
    ///    .send()
    ///    .await;
    /// ```
    pub fn get_holder_scan_result(&self) -> builder::GetHolderScanResult<'_> {
        builder::GetHolderScanResult::new(self)
    }
}

/// Types for composing operation parameters.
#[allow(clippy::all)]
pub mod builder {
    use super::types;
    #[allow(unused_imports)]
    use super::{
        encode_path, ByteStream, ClientHooks, ClientInfo, Error, OperationInfo, RequestBuilderExt,
        ResponseValue,
    };
    ///Builder for [`Client::get_chain_status`]
    ///
    ///[`Client::get_chain_status`]: super::Client::get_chain_status
    #[derive(Debug, Clone)]
    pub struct GetChainStatus<'a> {
        client: &'a super::Client,
        chain: Result<types::ChainId, String>,
    }

    impl<'a> GetChainStatus<'a> {
        pub fn new(client: &'a super::Client) -> Self {
            Self {
                client: client,
                chain: Err("chain was not initialized".to_string()),
            }
        }

        pub fn chain<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::ChainId>,
        {
            self.chain = value
                .try_into()
                .map_err(|_| "conversion to `ChainId` for chain failed".to_string());
            self
        }

        ///Sends a `GET` request to `/v1/{chain}/status`
        pub async fn send(
            self,
        ) -> Result<ResponseValue<types::StatusResponse>, Error<types::ErrorResponse>> {
            let Self { client, chain } = self;
            let chain = chain.map_err(Error::InvalidRequest)?;
            let url = format!(
                "{}/v1/{}/status",
                client.baseurl,
                encode_path(&chain.to_string()),
            );
            let mut header_map = ::reqwest::header::HeaderMap::with_capacity(1usize);
            header_map.append(
                ::reqwest::header::HeaderName::from_static("api-version"),
                ::reqwest::header::HeaderValue::from_static(super::Client::api_version()),
            );
            #[allow(unused_mut)]
            let mut request = client
                .client
                .get(url)
                .header(
                    ::reqwest::header::ACCEPT,
                    ::reqwest::header::HeaderValue::from_static("application/json"),
                )
                .headers(header_map)
                .build()?;
            let info = OperationInfo {
                operation_id: "get_chain_status",
            };
            client.pre(&mut request, &info).await?;
            let result = client.exec(request, &info).await;
            client.post(&result, &info).await?;
            let response = result?;
            match response.status().as_u16() {
                200u16 => ResponseValue::from_response(response).await,
                404u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                429u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                503u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                504u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                _ => Err(Error::UnexpectedResponse(response)),
            }
        }
    }

    ///Builder for [`Client::get_account_tokens`]
    ///
    ///[`Client::get_account_tokens`]: super::Client::get_account_tokens
    #[derive(Debug, Clone)]
    pub struct GetAccountTokens<'a> {
        client: &'a super::Client,
        chain: Result<types::ChainId, String>,
        address: Result<::std::string::String, String>,
        cursor: Result<Option<::std::string::String>, String>,
        include_zero: Result<Option<bool>, String>,
        limit: Result<Option<::std::num::NonZeroU32>, String>,
    }

    impl<'a> GetAccountTokens<'a> {
        pub fn new(client: &'a super::Client) -> Self {
            Self {
                client: client,
                chain: Err("chain was not initialized".to_string()),
                address: Err("address was not initialized".to_string()),
                cursor: Ok(None),
                include_zero: Ok(None),
                limit: Ok(None),
            }
        }

        pub fn chain<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::ChainId>,
        {
            self.chain = value
                .try_into()
                .map_err(|_| "conversion to `ChainId` for chain failed".to_string());
            self
        }

        pub fn address<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::string::String>,
        {
            self.address = value.try_into().map_err(|_| {
                "conversion to `:: std :: string :: String` for address failed".to_string()
            });
            self
        }

        pub fn cursor<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::string::String>,
        {
            self.cursor = value.try_into().map(Some).map_err(|_| {
                "conversion to `:: std :: string :: String` for cursor failed".to_string()
            });
            self
        }

        pub fn include_zero<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<bool>,
        {
            self.include_zero = value
                .try_into()
                .map(Some)
                .map_err(|_| "conversion to `bool` for include_zero failed".to_string());
            self
        }

        pub fn limit<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::num::NonZeroU32>,
        {
            self.limit = value.try_into().map(Some).map_err(|_| {
                "conversion to `:: std :: num :: NonZeroU32` for limit failed".to_string()
            });
            self
        }

        ///Sends a `GET` request to `/v1/{chain}/accounts/{address}/tokens`
        pub async fn send(
            self,
        ) -> Result<ResponseValue<types::HoldingsResponse>, Error<types::ErrorResponse>> {
            let Self {
                client,
                chain,
                address,
                cursor,
                include_zero,
                limit,
            } = self;
            let chain = chain.map_err(Error::InvalidRequest)?;
            let address = address.map_err(Error::InvalidRequest)?;
            let cursor = cursor.map_err(Error::InvalidRequest)?;
            let include_zero = include_zero.map_err(Error::InvalidRequest)?;
            let limit = limit.map_err(Error::InvalidRequest)?;
            let url = format!(
                "{}/v1/{}/accounts/{}/tokens",
                client.baseurl,
                encode_path(&chain.to_string()),
                encode_path(&address.to_string()),
            );
            let mut header_map = ::reqwest::header::HeaderMap::with_capacity(1usize);
            header_map.append(
                ::reqwest::header::HeaderName::from_static("api-version"),
                ::reqwest::header::HeaderValue::from_static(super::Client::api_version()),
            );
            #[allow(unused_mut)]
            let mut request = client
                .client
                .get(url)
                .header(
                    ::reqwest::header::ACCEPT,
                    ::reqwest::header::HeaderValue::from_static("application/json"),
                )
                .query(&progenitor_client::QueryParam::new("cursor", &cursor))
                .query(&progenitor_client::QueryParam::new(
                    "includeZero",
                    &include_zero,
                ))
                .query(&progenitor_client::QueryParam::new("limit", &limit))
                .headers(header_map)
                .build()?;
            let info = OperationInfo {
                operation_id: "get_account_tokens",
            };
            client.pre(&mut request, &info).await?;
            let result = client.exec(request, &info).await;
            client.post(&result, &info).await?;
            let response = result?;
            match response.status().as_u16() {
                200u16 => ResponseValue::from_response(response).await,
                400u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                429u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                503u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                504u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                _ => Err(Error::UnexpectedResponse(response)),
            }
        }
    }

    ///Builder for [`Client::get_token_balance`]
    ///
    ///[`Client::get_token_balance`]: super::Client::get_token_balance
    #[derive(Debug, Clone)]
    pub struct GetTokenBalance<'a> {
        client: &'a super::Client,
        chain: Result<types::ChainId, String>,
        address: Result<::std::string::String, String>,
        kind: Result<types::TokenKind, String>,
        token_id: Result<u64, String>,
        round: Result<Option<u64>, String>,
    }

    impl<'a> GetTokenBalance<'a> {
        pub fn new(client: &'a super::Client) -> Self {
            Self {
                client: client,
                chain: Err("chain was not initialized".to_string()),
                address: Err("address was not initialized".to_string()),
                kind: Err("kind was not initialized".to_string()),
                token_id: Err("token_id was not initialized".to_string()),
                round: Ok(None),
            }
        }

        pub fn chain<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::ChainId>,
        {
            self.chain = value
                .try_into()
                .map_err(|_| "conversion to `ChainId` for chain failed".to_string());
            self
        }

        pub fn address<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::string::String>,
        {
            self.address = value.try_into().map_err(|_| {
                "conversion to `:: std :: string :: String` for address failed".to_string()
            });
            self
        }

        pub fn kind<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::TokenKind>,
        {
            self.kind = value
                .try_into()
                .map_err(|_| "conversion to `TokenKind` for kind failed".to_string());
            self
        }

        pub fn token_id<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<u64>,
        {
            self.token_id = value
                .try_into()
                .map_err(|_| "conversion to `u64` for token_id failed".to_string());
            self
        }

        pub fn round<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<u64>,
        {
            self.round = value
                .try_into()
                .map(Some)
                .map_err(|_| "conversion to `u64` for round failed".to_string());
            self
        }

        ///Sends a `GET` request to
        /// `/v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/balance`
        pub async fn send(
            self,
        ) -> Result<ResponseValue<types::BalanceResponse>, Error<types::ErrorResponse>> {
            let Self {
                client,
                chain,
                address,
                kind,
                token_id,
                round,
            } = self;
            let chain = chain.map_err(Error::InvalidRequest)?;
            let address = address.map_err(Error::InvalidRequest)?;
            let kind = kind.map_err(Error::InvalidRequest)?;
            let token_id = token_id.map_err(Error::InvalidRequest)?;
            let round = round.map_err(Error::InvalidRequest)?;
            let url = format!(
                "{}/v1/{}/accounts/{}/tokens/{}/{}/balance",
                client.baseurl,
                encode_path(&chain.to_string()),
                encode_path(&address.to_string()),
                encode_path(&kind.to_string()),
                encode_path(&token_id.to_string()),
            );
            let mut header_map = ::reqwest::header::HeaderMap::with_capacity(1usize);
            header_map.append(
                ::reqwest::header::HeaderName::from_static("api-version"),
                ::reqwest::header::HeaderValue::from_static(super::Client::api_version()),
            );
            #[allow(unused_mut)]
            let mut request = client
                .client
                .get(url)
                .header(
                    ::reqwest::header::ACCEPT,
                    ::reqwest::header::HeaderValue::from_static("application/json"),
                )
                .query(&progenitor_client::QueryParam::new("round", &round))
                .headers(header_map)
                .build()?;
            let info = OperationInfo {
                operation_id: "get_token_balance",
            };
            client.pre(&mut request, &info).await?;
            let result = client.exec(request, &info).await;
            client.post(&result, &info).await?;
            let response = result?;
            match response.status().as_u16() {
                200u16 => ResponseValue::from_response(response).await,
                400u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                404u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                429u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                503u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                504u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                _ => Err(Error::UnexpectedResponse(response)),
            }
        }
    }

    ///Builder for [`Client::get_token_history`]
    ///
    ///[`Client::get_token_history`]: super::Client::get_token_history
    #[derive(Debug, Clone)]
    pub struct GetTokenHistory<'a> {
        client: &'a super::Client,
        chain: Result<types::ChainId, String>,
        address: Result<::std::string::String, String>,
        kind: Result<types::TokenKind, String>,
        token_id: Result<u64, String>,
        cursor: Result<Option<::std::string::String>, String>,
        from_round: Result<Option<u64>, String>,
        limit: Result<Option<::std::num::NonZeroU32>, String>,
        to_round: Result<Option<u64>, String>,
    }

    impl<'a> GetTokenHistory<'a> {
        pub fn new(client: &'a super::Client) -> Self {
            Self {
                client: client,
                chain: Err("chain was not initialized".to_string()),
                address: Err("address was not initialized".to_string()),
                kind: Err("kind was not initialized".to_string()),
                token_id: Err("token_id was not initialized".to_string()),
                cursor: Ok(None),
                from_round: Ok(None),
                limit: Ok(None),
                to_round: Ok(None),
            }
        }

        pub fn chain<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::ChainId>,
        {
            self.chain = value
                .try_into()
                .map_err(|_| "conversion to `ChainId` for chain failed".to_string());
            self
        }

        pub fn address<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::string::String>,
        {
            self.address = value.try_into().map_err(|_| {
                "conversion to `:: std :: string :: String` for address failed".to_string()
            });
            self
        }

        pub fn kind<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::TokenKind>,
        {
            self.kind = value
                .try_into()
                .map_err(|_| "conversion to `TokenKind` for kind failed".to_string());
            self
        }

        pub fn token_id<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<u64>,
        {
            self.token_id = value
                .try_into()
                .map_err(|_| "conversion to `u64` for token_id failed".to_string());
            self
        }

        pub fn cursor<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::string::String>,
        {
            self.cursor = value.try_into().map(Some).map_err(|_| {
                "conversion to `:: std :: string :: String` for cursor failed".to_string()
            });
            self
        }

        pub fn from_round<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<u64>,
        {
            self.from_round = value
                .try_into()
                .map(Some)
                .map_err(|_| "conversion to `u64` for from_round failed".to_string());
            self
        }

        pub fn limit<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::num::NonZeroU32>,
        {
            self.limit = value.try_into().map(Some).map_err(|_| {
                "conversion to `:: std :: num :: NonZeroU32` for limit failed".to_string()
            });
            self
        }

        pub fn to_round<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<u64>,
        {
            self.to_round = value
                .try_into()
                .map(Some)
                .map_err(|_| "conversion to `u64` for to_round failed".to_string());
            self
        }

        ///Sends a `GET` request to
        /// `/v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/
        /// transactions`
        pub async fn send(
            self,
        ) -> Result<ResponseValue<types::HistoryResponse>, Error<types::ErrorResponse>> {
            let Self {
                client,
                chain,
                address,
                kind,
                token_id,
                cursor,
                from_round,
                limit,
                to_round,
            } = self;
            let chain = chain.map_err(Error::InvalidRequest)?;
            let address = address.map_err(Error::InvalidRequest)?;
            let kind = kind.map_err(Error::InvalidRequest)?;
            let token_id = token_id.map_err(Error::InvalidRequest)?;
            let cursor = cursor.map_err(Error::InvalidRequest)?;
            let from_round = from_round.map_err(Error::InvalidRequest)?;
            let limit = limit.map_err(Error::InvalidRequest)?;
            let to_round = to_round.map_err(Error::InvalidRequest)?;
            let url = format!(
                "{}/v1/{}/accounts/{}/tokens/{}/{}/transactions",
                client.baseurl,
                encode_path(&chain.to_string()),
                encode_path(&address.to_string()),
                encode_path(&kind.to_string()),
                encode_path(&token_id.to_string()),
            );
            let mut header_map = ::reqwest::header::HeaderMap::with_capacity(1usize);
            header_map.append(
                ::reqwest::header::HeaderName::from_static("api-version"),
                ::reqwest::header::HeaderValue::from_static(super::Client::api_version()),
            );
            #[allow(unused_mut)]
            let mut request = client
                .client
                .get(url)
                .header(
                    ::reqwest::header::ACCEPT,
                    ::reqwest::header::HeaderValue::from_static("application/json"),
                )
                .query(&progenitor_client::QueryParam::new("cursor", &cursor))
                .query(&progenitor_client::QueryParam::new(
                    "fromRound",
                    &from_round,
                ))
                .query(&progenitor_client::QueryParam::new("limit", &limit))
                .query(&progenitor_client::QueryParam::new("toRound", &to_round))
                .headers(header_map)
                .build()?;
            let info = OperationInfo {
                operation_id: "get_token_history",
            };
            client.pre(&mut request, &info).await?;
            let result = client.exec(request, &info).await;
            client.post(&result, &info).await?;
            let response = result?;
            match response.status().as_u16() {
                200u16 => ResponseValue::from_response(response).await,
                400u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                429u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                503u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                504u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                _ => Err(Error::UnexpectedResponse(response)),
            }
        }
    }

    ///Builder for [`Client::get_token_holders`]
    ///
    ///[`Client::get_token_holders`]: super::Client::get_token_holders
    #[derive(Debug, Clone)]
    pub struct GetTokenHolders<'a> {
        client: &'a super::Client,
        chain: Result<types::ChainId, String>,
        kind: Result<types::TokenKind, String>,
        token_id: Result<u64, String>,
        cursor: Result<Option<::std::string::String>, String>,
        include_zero: Result<Option<bool>, String>,
        limit: Result<Option<::std::num::NonZeroU32>, String>,
        round: Result<Option<u64>, String>,
    }

    impl<'a> GetTokenHolders<'a> {
        pub fn new(client: &'a super::Client) -> Self {
            Self {
                client: client,
                chain: Err("chain was not initialized".to_string()),
                kind: Err("kind was not initialized".to_string()),
                token_id: Err("token_id was not initialized".to_string()),
                cursor: Ok(None),
                include_zero: Ok(None),
                limit: Ok(None),
                round: Ok(None),
            }
        }

        pub fn chain<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::ChainId>,
        {
            self.chain = value
                .try_into()
                .map_err(|_| "conversion to `ChainId` for chain failed".to_string());
            self
        }

        pub fn kind<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::TokenKind>,
        {
            self.kind = value
                .try_into()
                .map_err(|_| "conversion to `TokenKind` for kind failed".to_string());
            self
        }

        pub fn token_id<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<u64>,
        {
            self.token_id = value
                .try_into()
                .map_err(|_| "conversion to `u64` for token_id failed".to_string());
            self
        }

        pub fn cursor<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::string::String>,
        {
            self.cursor = value.try_into().map(Some).map_err(|_| {
                "conversion to `:: std :: string :: String` for cursor failed".to_string()
            });
            self
        }

        pub fn include_zero<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<bool>,
        {
            self.include_zero = value
                .try_into()
                .map(Some)
                .map_err(|_| "conversion to `bool` for include_zero failed".to_string());
            self
        }

        pub fn limit<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::num::NonZeroU32>,
        {
            self.limit = value.try_into().map(Some).map_err(|_| {
                "conversion to `:: std :: num :: NonZeroU32` for limit failed".to_string()
            });
            self
        }

        pub fn round<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<u64>,
        {
            self.round = value
                .try_into()
                .map(Some)
                .map_err(|_| "conversion to `u64` for round failed".to_string());
            self
        }

        ///Sends a `GET` request to
        /// `/v1/{chain}/tokens/{kind}/{tokenId}/holders`
        pub async fn send(
            self,
        ) -> Result<ResponseValue<types::HoldersResponse>, Error<types::ErrorResponse>> {
            let Self {
                client,
                chain,
                kind,
                token_id,
                cursor,
                include_zero,
                limit,
                round,
            } = self;
            let chain = chain.map_err(Error::InvalidRequest)?;
            let kind = kind.map_err(Error::InvalidRequest)?;
            let token_id = token_id.map_err(Error::InvalidRequest)?;
            let cursor = cursor.map_err(Error::InvalidRequest)?;
            let include_zero = include_zero.map_err(Error::InvalidRequest)?;
            let limit = limit.map_err(Error::InvalidRequest)?;
            let round = round.map_err(Error::InvalidRequest)?;
            let url = format!(
                "{}/v1/{}/tokens/{}/{}/holders",
                client.baseurl,
                encode_path(&chain.to_string()),
                encode_path(&kind.to_string()),
                encode_path(&token_id.to_string()),
            );
            let mut header_map = ::reqwest::header::HeaderMap::with_capacity(1usize);
            header_map.append(
                ::reqwest::header::HeaderName::from_static("api-version"),
                ::reqwest::header::HeaderValue::from_static(super::Client::api_version()),
            );
            #[allow(unused_mut)]
            let mut request = client
                .client
                .get(url)
                .header(
                    ::reqwest::header::ACCEPT,
                    ::reqwest::header::HeaderValue::from_static("application/json"),
                )
                .query(&progenitor_client::QueryParam::new("cursor", &cursor))
                .query(&progenitor_client::QueryParam::new(
                    "includeZero",
                    &include_zero,
                ))
                .query(&progenitor_client::QueryParam::new("limit", &limit))
                .query(&progenitor_client::QueryParam::new("round", &round))
                .headers(header_map)
                .build()?;
            let info = OperationInfo {
                operation_id: "get_token_holders",
            };
            client.pre(&mut request, &info).await?;
            let result = client.exec(request, &info).await;
            client.post(&result, &info).await?;
            let response = result?;
            match response.status().as_u16() {
                200u16 => ResponseValue::from_response(response).await,
                400u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                404u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                429u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                503u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                504u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                _ => Err(Error::UnexpectedResponse(response)),
            }
        }
    }

    ///Builder for [`Client::get_token`]
    ///
    ///[`Client::get_token`]: super::Client::get_token
    #[derive(Debug, Clone)]
    pub struct GetToken<'a> {
        client: &'a super::Client,
        chain: Result<types::ChainId, String>,
        kind: Result<types::TokenKind, String>,
        token_id: Result<u64, String>,
    }

    impl<'a> GetToken<'a> {
        pub fn new(client: &'a super::Client) -> Self {
            Self {
                client: client,
                chain: Err("chain was not initialized".to_string()),
                kind: Err("kind was not initialized".to_string()),
                token_id: Err("token_id was not initialized".to_string()),
            }
        }

        pub fn chain<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::ChainId>,
        {
            self.chain = value
                .try_into()
                .map_err(|_| "conversion to `ChainId` for chain failed".to_string());
            self
        }

        pub fn kind<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::TokenKind>,
        {
            self.kind = value
                .try_into()
                .map_err(|_| "conversion to `TokenKind` for kind failed".to_string());
            self
        }

        pub fn token_id<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<u64>,
        {
            self.token_id = value
                .try_into()
                .map_err(|_| "conversion to `u64` for token_id failed".to_string());
            self
        }

        ///Sends a `GET` request to `/v1/{chain}/tokens/{kind}/{tokenId}`
        pub async fn send(
            self,
        ) -> Result<ResponseValue<types::TokenResponse>, Error<types::ErrorResponse>> {
            let Self {
                client,
                chain,
                kind,
                token_id,
            } = self;
            let chain = chain.map_err(Error::InvalidRequest)?;
            let kind = kind.map_err(Error::InvalidRequest)?;
            let token_id = token_id.map_err(Error::InvalidRequest)?;
            let url = format!(
                "{}/v1/{}/tokens/{}/{}",
                client.baseurl,
                encode_path(&chain.to_string()),
                encode_path(&kind.to_string()),
                encode_path(&token_id.to_string()),
            );
            let mut header_map = ::reqwest::header::HeaderMap::with_capacity(1usize);
            header_map.append(
                ::reqwest::header::HeaderName::from_static("api-version"),
                ::reqwest::header::HeaderValue::from_static(super::Client::api_version()),
            );
            #[allow(unused_mut)]
            let mut request = client
                .client
                .get(url)
                .header(
                    ::reqwest::header::ACCEPT,
                    ::reqwest::header::HeaderValue::from_static("application/json"),
                )
                .headers(header_map)
                .build()?;
            let info = OperationInfo {
                operation_id: "get_token",
            };
            client.pre(&mut request, &info).await?;
            let result = client.exec(request, &info).await;
            client.post(&result, &info).await?;
            let response = result?;
            match response.status().as_u16() {
                200u16 => ResponseValue::from_response(response).await,
                400u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                404u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                429u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                503u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                504u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                _ => Err(Error::UnexpectedResponse(response)),
            }
        }
    }

    ///Builder for [`Client::create_holder_scan`]
    ///
    ///[`Client::create_holder_scan`]: super::Client::create_holder_scan
    #[derive(Debug, Clone)]
    pub struct CreateHolderScan<'a> {
        client: &'a super::Client,
        chain: Result<types::ChainId, String>,
        kind: Result<types::TokenKind, String>,
        token_id: Result<u64, String>,
        body: Result<types::builder::ScanRequest, String>,
    }

    impl<'a> CreateHolderScan<'a> {
        pub fn new(client: &'a super::Client) -> Self {
            Self {
                client: client,
                chain: Err("chain was not initialized".to_string()),
                kind: Err("kind was not initialized".to_string()),
                token_id: Err("token_id was not initialized".to_string()),
                body: Ok(::std::default::Default::default()),
            }
        }

        pub fn chain<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::ChainId>,
        {
            self.chain = value
                .try_into()
                .map_err(|_| "conversion to `ChainId` for chain failed".to_string());
            self
        }

        pub fn kind<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::TokenKind>,
        {
            self.kind = value
                .try_into()
                .map_err(|_| "conversion to `TokenKind` for kind failed".to_string());
            self
        }

        pub fn token_id<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<u64>,
        {
            self.token_id = value
                .try_into()
                .map_err(|_| "conversion to `u64` for token_id failed".to_string());
            self
        }

        pub fn body<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::ScanRequest>,
            <V as std::convert::TryInto<types::ScanRequest>>::Error: std::fmt::Display,
        {
            self.body = value
                .try_into()
                .map(From::from)
                .map_err(|s| format!("conversion to `ScanRequest` for body failed: {}", s));
            self
        }

        pub fn body_map<F>(mut self, f: F) -> Self
        where
            F: std::ops::FnOnce(types::builder::ScanRequest) -> types::builder::ScanRequest,
        {
            self.body = self.body.map(f);
            self
        }

        ///Sends a `POST` request to
        /// `/v1/{chain}/tokens/{kind}/{tokenId}/holders/scans`
        pub async fn send(
            self,
        ) -> Result<ResponseValue<types::ScanResponse>, Error<types::ErrorResponse>> {
            let Self {
                client,
                chain,
                kind,
                token_id,
                body,
            } = self;
            let chain = chain.map_err(Error::InvalidRequest)?;
            let kind = kind.map_err(Error::InvalidRequest)?;
            let token_id = token_id.map_err(Error::InvalidRequest)?;
            let body = body
                .and_then(|v| types::ScanRequest::try_from(v).map_err(|e| e.to_string()))
                .map_err(Error::InvalidRequest)?;
            let url = format!(
                "{}/v1/{}/tokens/{}/{}/holders/scans",
                client.baseurl,
                encode_path(&chain.to_string()),
                encode_path(&kind.to_string()),
                encode_path(&token_id.to_string()),
            );
            let mut header_map = ::reqwest::header::HeaderMap::with_capacity(1usize);
            header_map.append(
                ::reqwest::header::HeaderName::from_static("api-version"),
                ::reqwest::header::HeaderValue::from_static(super::Client::api_version()),
            );
            #[allow(unused_mut)]
            let mut request = client
                .client
                .post(url)
                .header(
                    ::reqwest::header::ACCEPT,
                    ::reqwest::header::HeaderValue::from_static("application/json"),
                )
                .json(&body)
                .headers(header_map)
                .build()?;
            let info = OperationInfo {
                operation_id: "create_holder_scan",
            };
            client.pre(&mut request, &info).await?;
            let result = client.exec(request, &info).await;
            client.post(&result, &info).await?;
            let response = result?;
            match response.status().as_u16() {
                202u16 => ResponseValue::from_response(response).await,
                400u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                404u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                429u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                503u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                504u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                _ => Err(Error::UnexpectedResponse(response)),
            }
        }
    }

    ///Builder for [`Client::get_holder_scan`]
    ///
    ///[`Client::get_holder_scan`]: super::Client::get_holder_scan
    #[derive(Debug, Clone)]
    pub struct GetHolderScan<'a> {
        client: &'a super::Client,
        chain: Result<types::ChainId, String>,
        id: Result<::std::string::String, String>,
    }

    impl<'a> GetHolderScan<'a> {
        pub fn new(client: &'a super::Client) -> Self {
            Self {
                client: client,
                chain: Err("chain was not initialized".to_string()),
                id: Err("id was not initialized".to_string()),
            }
        }

        pub fn chain<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::ChainId>,
        {
            self.chain = value
                .try_into()
                .map_err(|_| "conversion to `ChainId` for chain failed".to_string());
            self
        }

        pub fn id<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::string::String>,
        {
            self.id = value.try_into().map_err(|_| {
                "conversion to `:: std :: string :: String` for id failed".to_string()
            });
            self
        }

        ///Sends a `GET` request to `/v1/{chain}/scans/{id}`
        pub async fn send(
            self,
        ) -> Result<ResponseValue<types::ScanResponse>, Error<types::ErrorResponse>> {
            let Self { client, chain, id } = self;
            let chain = chain.map_err(Error::InvalidRequest)?;
            let id = id.map_err(Error::InvalidRequest)?;
            let url = format!(
                "{}/v1/{}/scans/{}",
                client.baseurl,
                encode_path(&chain.to_string()),
                encode_path(&id.to_string()),
            );
            let mut header_map = ::reqwest::header::HeaderMap::with_capacity(1usize);
            header_map.append(
                ::reqwest::header::HeaderName::from_static("api-version"),
                ::reqwest::header::HeaderValue::from_static(super::Client::api_version()),
            );
            #[allow(unused_mut)]
            let mut request = client
                .client
                .get(url)
                .header(
                    ::reqwest::header::ACCEPT,
                    ::reqwest::header::HeaderValue::from_static("application/json"),
                )
                .headers(header_map)
                .build()?;
            let info = OperationInfo {
                operation_id: "get_holder_scan",
            };
            client.pre(&mut request, &info).await?;
            let result = client.exec(request, &info).await;
            client.post(&result, &info).await?;
            let response = result?;
            match response.status().as_u16() {
                200u16 => ResponseValue::from_response(response).await,
                400u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                404u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                429u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                503u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                504u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                _ => Err(Error::UnexpectedResponse(response)),
            }
        }
    }

    ///Builder for [`Client::cancel_holder_scan`]
    ///
    ///[`Client::cancel_holder_scan`]: super::Client::cancel_holder_scan
    #[derive(Debug, Clone)]
    pub struct CancelHolderScan<'a> {
        client: &'a super::Client,
        chain: Result<types::ChainId, String>,
        id: Result<::std::string::String, String>,
    }

    impl<'a> CancelHolderScan<'a> {
        pub fn new(client: &'a super::Client) -> Self {
            Self {
                client: client,
                chain: Err("chain was not initialized".to_string()),
                id: Err("id was not initialized".to_string()),
            }
        }

        pub fn chain<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::ChainId>,
        {
            self.chain = value
                .try_into()
                .map_err(|_| "conversion to `ChainId` for chain failed".to_string());
            self
        }

        pub fn id<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::string::String>,
        {
            self.id = value.try_into().map_err(|_| {
                "conversion to `:: std :: string :: String` for id failed".to_string()
            });
            self
        }

        ///Sends a `DELETE` request to `/v1/{chain}/scans/{id}`
        pub async fn send(
            self,
        ) -> Result<ResponseValue<types::ScanResponse>, Error<types::ErrorResponse>> {
            let Self { client, chain, id } = self;
            let chain = chain.map_err(Error::InvalidRequest)?;
            let id = id.map_err(Error::InvalidRequest)?;
            let url = format!(
                "{}/v1/{}/scans/{}",
                client.baseurl,
                encode_path(&chain.to_string()),
                encode_path(&id.to_string()),
            );
            let mut header_map = ::reqwest::header::HeaderMap::with_capacity(1usize);
            header_map.append(
                ::reqwest::header::HeaderName::from_static("api-version"),
                ::reqwest::header::HeaderValue::from_static(super::Client::api_version()),
            );
            #[allow(unused_mut)]
            let mut request = client
                .client
                .delete(url)
                .header(
                    ::reqwest::header::ACCEPT,
                    ::reqwest::header::HeaderValue::from_static("application/json"),
                )
                .headers(header_map)
                .build()?;
            let info = OperationInfo {
                operation_id: "cancel_holder_scan",
            };
            client.pre(&mut request, &info).await?;
            let result = client.exec(request, &info).await;
            client.post(&result, &info).await?;
            let response = result?;
            match response.status().as_u16() {
                200u16 => ResponseValue::from_response(response).await,
                400u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                404u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                409u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                429u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                503u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                504u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                _ => Err(Error::UnexpectedResponse(response)),
            }
        }
    }

    ///Builder for [`Client::get_holder_scan_result`]
    ///
    ///[`Client::get_holder_scan_result`]: super::Client::get_holder_scan_result
    #[derive(Debug, Clone)]
    pub struct GetHolderScanResult<'a> {
        client: &'a super::Client,
        chain: Result<types::ChainId, String>,
        id: Result<::std::string::String, String>,
        cursor: Result<Option<::std::string::String>, String>,
        limit: Result<Option<::std::num::NonZeroU32>, String>,
    }

    impl<'a> GetHolderScanResult<'a> {
        pub fn new(client: &'a super::Client) -> Self {
            Self {
                client: client,
                chain: Err("chain was not initialized".to_string()),
                id: Err("id was not initialized".to_string()),
                cursor: Ok(None),
                limit: Ok(None),
            }
        }

        pub fn chain<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<types::ChainId>,
        {
            self.chain = value
                .try_into()
                .map_err(|_| "conversion to `ChainId` for chain failed".to_string());
            self
        }

        pub fn id<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::string::String>,
        {
            self.id = value.try_into().map_err(|_| {
                "conversion to `:: std :: string :: String` for id failed".to_string()
            });
            self
        }

        pub fn cursor<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::string::String>,
        {
            self.cursor = value.try_into().map(Some).map_err(|_| {
                "conversion to `:: std :: string :: String` for cursor failed".to_string()
            });
            self
        }

        pub fn limit<V>(mut self, value: V) -> Self
        where
            V: std::convert::TryInto<::std::num::NonZeroU32>,
        {
            self.limit = value.try_into().map(Some).map_err(|_| {
                "conversion to `:: std :: num :: NonZeroU32` for limit failed".to_string()
            });
            self
        }

        ///Sends a `GET` request to `/v1/{chain}/scans/{id}/holders`
        pub async fn send(
            self,
        ) -> Result<ResponseValue<types::ScanHoldersResponse>, Error<types::ErrorResponse>>
        {
            let Self {
                client,
                chain,
                id,
                cursor,
                limit,
            } = self;
            let chain = chain.map_err(Error::InvalidRequest)?;
            let id = id.map_err(Error::InvalidRequest)?;
            let cursor = cursor.map_err(Error::InvalidRequest)?;
            let limit = limit.map_err(Error::InvalidRequest)?;
            let url = format!(
                "{}/v1/{}/scans/{}/holders",
                client.baseurl,
                encode_path(&chain.to_string()),
                encode_path(&id.to_string()),
            );
            let mut header_map = ::reqwest::header::HeaderMap::with_capacity(1usize);
            header_map.append(
                ::reqwest::header::HeaderName::from_static("api-version"),
                ::reqwest::header::HeaderValue::from_static(super::Client::api_version()),
            );
            #[allow(unused_mut)]
            let mut request = client
                .client
                .get(url)
                .header(
                    ::reqwest::header::ACCEPT,
                    ::reqwest::header::HeaderValue::from_static("application/json"),
                )
                .query(&progenitor_client::QueryParam::new("cursor", &cursor))
                .query(&progenitor_client::QueryParam::new("limit", &limit))
                .headers(header_map)
                .build()?;
            let info = OperationInfo {
                operation_id: "get_holder_scan_result",
            };
            client.pre(&mut request, &info).await?;
            let result = client.exec(request, &info).await;
            client.post(&result, &info).await?;
            let response = result?;
            match response.status().as_u16() {
                200u16 => ResponseValue::from_response(response).await,
                400u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                404u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                429u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                503u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                504u16 => Err(Error::ErrorResponse(
                    ResponseValue::from_response(response).await?,
                )),
                _ => Err(Error::UnexpectedResponse(response)),
            }
        }
    }
}

/// Items consumers will typically use such as the Client.
pub mod prelude {
    pub use self::super::Client;
}
