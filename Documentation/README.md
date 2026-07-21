# ln-history-api

## API

### Model / DTOs

#### FeePolicyDto
```json
{
	cltv_expiry_delta: int,
	channel_flags: str,
	fee_base_msat: long,
	fee_proportional_millionths: long,
	htlc_minimum_msat: long,
	htlc_maximum_msat: long | null,
}
```

#### ChannelUpdateDto
```json
{
	scid: int,
	scid_str: str,
	direction: boolean,
	source_node_id: str,
	target_node_id: str,
	valid_from: DateTime,
	valid_to: DateTime,
	fee_policy: FeePolicyDto,
	timestamp: DateTime,
	message_flags: str,
	is_topology_update: boolean,
	is_fee_update: boolean,
	gossip_id: str,
	internal_id: int,
	raw_bytes: bytes[] | null
}
```

#### ChannelClosureDto
```json
{
	scid: int,
	scid_str: str,
	closure_type: ClosureType,
	mining_fee: int,
	txid: str,
	tx: bytes[] | null
}
```


#### ChannelDto
```json
{
	scid: int,
	scid_str: str,
	funding_timestamp: DateTime,
	closing_timestamp: DateTime | null,
	closing_information: ChannelClosureDto,
	capacity_sat: int,
	node_id_1: str | NodeDto,
	node_id_2: str | NodeDto,
	fee_policies: 
		{
			"0": 
				{
					fee_policy: FeePolicyDto,
					total_update_count: int
				},
			"1": 
				{
					fee_policy: FeePolicyDto,
					total_update_count: int	
				}
		},
	gossip_id: str,
	internal_id: int,
	raw_bytes: bytes[] | null
}
```

#### AddressTypeDto
```json
{
	id: int,
	name: str,
	description: str
}
```

#### AddressDto
```json
{
	id: int,
	network: AddressTypeDto,
	address: str,
	port: str
}
```

#### NodeAnnouncementDto
```json
{
	node_id: str,
	alias: str,
	rgb_color: str,
	features: str,
	addresses: AddressDto[],
	timestamp: DateTime,
	is_data_update: boolean,
	gossip_id: str,
	internal_id: int,
	raw_gossip: bytes[]
}
```

#### NodeDto
```json
{
	node_id: str,
	first_seen: DateTime,
	last_seen: DateTime,
	number_of_channels: int,
	number_of_announcements: int,
	announcements: NodeAnnouncementDto
}
```

#### BlockDto
```json
{
	block_hash: str,
	block_height: int,
	timestamp: DateTime,
	space_bytes: int,
	subsidy_sat: int,
	tx_fees: int
}
```


#### PeerDto
```json
{
	
}
```

## Controller 

### LightningNetwork
- /snapshot/{DateTime}?withUpdates={boolean} -> bytes[] (All gossip valid at DateTime
- /snapshot-diff/{startDateTime}/{endDateTime}?rawGossip={boolean} -> raw_gossip that happened between startDateTime and endDateTime

### Channel
- /channels/{DateTime} -> ChannelDto[]
- /channels/{scid:str}?nodeInformation={boolean}&raw_gossip={boolean}&timestamp={DateTime}  -> ChannelDto
- /channel/{scid:int}?nodeInformation={boolean}&raw_gossip={boolean}&timestamp={DateTime} -> ChannelDto
- /channel/history/{scid}?raw=true&timestamp={DateTime} -> bytes[]
- /channel/history/{scid}?raw=false&timestamp={DateTime} ->  ChannelDto and `fee_policy` is FeePolicyDto[]
- /channels/{node_id}?raw_gossip={true/false}&timestamp={DateTime} -> ChannelDto[]

### Node
- /nodes/{node_id} -> NodeDto[]
- /nodes/history/{node_id}?raw=false -> NodeDto[]
- /nodes/history/{node_id}?raw=true -> bytes[]
- /nodes/{DateTime}

### Bitcoin
- /blocks/{blockHash} -> BlockDto
- /blocks/{blockHeight} -> BlockDto
- /blocks/{DateTime} -> BlockDto


## 
- Update to dotnet 10
- Fix vulnerable dependencies
