﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Allows for damaging tiles and updating tiles based on damage taken.
/// </summary>
public class TilemapDamage : MonoBehaviour, IFireExposable
{
	private const int MAX_TABLE_DAMAGE = 50;
	private const int MAX_WALL_DAMAGE = 100;
	private const int MAX_FLOOR_DAMAGE = 70;
	private const int MAX_PLATING_DAMAGE = 100;
	private const int MAX_WINDOW_DAMAGE = 100;
	private const int MAX_GRILL_DAMAGE = 60;
	private static readonly float TILE_MIN_SCORCH_TEMPERATURE = 100f;

	private TileChangeManager tileChangeManager;

	private MetaDataLayer metaDataLayer;
	private MetaTileMap metaTileMap;

	public Layer Layer { get; private set; }

	private Matrix matrix;

	//armor for windows and grills
	private static readonly Armor REINFORCED_WINDOW_ARMOR = new Armor
	{
		Melee = 50,
		Bullet = 0,
		Laser = 0,
		Energy = 0,
		Bomb = 25,
		Bio = 100,
		Rad = 100,
		Fire = 80,
		Acid = 100
	};
	private static readonly Armor GRILL_ARMOR = new Armor
	{
		Melee = 50,
		Bullet = 70,
		Laser = 70,
		Energy = 100,
		Bomb = 10,
		Bio = 100,
		Rad = 100,
		Fire = 0,
		Acid = 0
	};
	//these are semi-random, please tweak
	private static readonly Armor FLOOR_ARMOR = new Armor
	{
		Melee = 50,
		Bullet = 50,
		Laser = 50,
		Energy = 50,
		Bomb = 10,
		Bio = 50,
		Rad = 100,
		Fire = 0,
		Acid = 0
	};
	private static readonly Armor WALL_ARMOR = new Armor
	{
		Melee = 90,
		Bullet = 90,
		Laser = 90,
		Energy = 90,
		Bomb = 90,
		Bio = 100,
		Rad = 100,
		Fire = 100,
		Acid = 90
	};
	private static readonly Armor TABLE_ARMOR = new Armor
	{
		Melee = 20,
		Bullet = 20,
		Laser = 20,
		Energy = 20,
		Bomb = 10,
		Bio = 100,
		Rad = 100,
		Fire = 0,
		Acid = 0
	};
	private static readonly Armor BASE_ARMOR = new Armor
	{
		Melee = 50,
		Bullet = 40,
		Laser = 10,
		Energy = 10,
		Bomb = 30,
		Bio = 50,
		Rad = 100,
		Fire = 80,
		Acid = 50
	};

	void Awake()
	{
		tileChangeManager = transform.GetComponentInParent<TileChangeManager>();
		metaDataLayer = transform.GetComponentInParent<MetaDataLayer>();
		metaTileMap = transform.GetComponentInParent<MetaTileMap>();

		Layer = GetComponent<Layer>();
		matrix = GetComponentInParent<Matrix>();
	}

	//Server Only:
	public void OnCollisionEnter2D(Collision2D coll)
	{
		if (!CustomNetworkManager.Instance._isServer)
		{
			return;
		}
		ContactPoint2D firstContact = coll.GetContact(0);
		Vector2 dirOfForce = (firstContact.point - (Vector2) coll.transform.position).normalized;
		DetermineAction(coll.gameObject, dirOfForce, firstContact.point);
	}

	private void DetermineAction(GameObject objectColliding, Vector2 forceDirection, Vector3 hitPos)
	{
		BulletBehaviour bulletBehaviour = objectColliding.transform.parent.GetComponent<BulletBehaviour>();
		if (bulletBehaviour != null)
		{
			DoBulletDamage(bulletBehaviour, forceDirection, hitPos);
		}
	}

	private void DoBulletDamage(BulletBehaviour bullet, Vector3 forceDir, Vector3 hitPos)
	{
		forceDir.z = 0;
		Vector3 bulletHitTarget = hitPos + (forceDir * 0.2f);
		Vector3Int cellPos = metaTileMap.WorldToCell(Vector3Int.RoundToInt(bulletHitTarget));
		MetaDataNode data = metaDataLayer.Get(cellPos);

		if (Layer.LayerType == LayerType.Windows)
		{
			LayerTile getTile = metaTileMap.GetTile(cellPos, LayerType.Windows);
			if (getTile != null)
			{
				//TODO damage amt based off type of bullet
				AddWindowDamage(bullet.damage, data, cellPos, bulletHitTarget, AttackType.Bullet);
				return;
			}
		}

		if (Layer.LayerType == LayerType.Grills)
		{
			//Make sure a window is not protecting it first:
			if (!metaTileMap.HasTile(cellPos, LayerType.Windows, true))
			{
				if (metaTileMap.HasTile(cellPos, LayerType.Grills, true))
				{
					//TODO damage amt based off type of bullet
					AddGrillDamage(bullet.damage, data, cellPos, bulletHitTarget, AttackType.Bullet);
				}
			}
		}
	}

	public void DoThrowDamage(Vector3Int worldTargetPos, ThrowInfo throwInfo, int dmgAmt)
	{
		DoMeleeDamage(new Vector2(worldTargetPos.x, worldTargetPos.y), throwInfo.ThrownBy, dmgAmt);
	}

	//Only works serverside:
	public void DoMeleeDamage(Vector2 worldPos, GameObject originator, int dmgAmt)
	{
		Vector3Int cellPos = metaTileMap.WorldToCell(worldPos);
		DoDamageInternal( cellPos, dmgAmt, worldPos, AttackType.Melee );
	}

	public float ApplyDamage( Vector3Int cellPos, float dmgAmt, Vector3Int worldPos )
	{
		return DoDamageInternal( cellPos, dmgAmt, worldPos, AttackType.Melee ); //idk if collision can be classified as "melee"
	}

	/// <returns>Unapplied damage</returns>
	private float DoDamageInternal( Vector3Int cellPos, float dmgAmt, Vector3 worldPos, AttackType attackType )
	{
		MetaDataNode data = metaDataLayer.Get( cellPos );

		if ( Layer.LayerType == LayerType.Walls )
		{
			if ( metaTileMap.HasTile( cellPos, LayerType.Walls, true ) )
			{
//				SoundManager.PlayNetworkedAtPos( "WallHit", worldPos, Random.Range( 0.9f, 1.1f ) );
				return AddWallDamage( dmgAmt, data, cellPos, worldPos, attackType );
			}
		}

		if ( Layer.LayerType == LayerType.Windows )
		{
			if ( metaTileMap.HasTile( cellPos, LayerType.Windows, true ) )
			{
				SoundManager.PlayNetworkedAtPos( "GlassHit", worldPos, Random.Range( 0.9f, 1.1f ) );
				return AddWindowDamage( dmgAmt, data, cellPos, worldPos, attackType );
			}
		}

		if ( Layer.LayerType == LayerType.Grills )
		{
			//Make sure a window is not protecting it first:
			if ( !metaTileMap.HasTile( cellPos, LayerType.Windows, true ) )
			{
				if ( metaTileMap.HasTile( cellPos, LayerType.Grills, true ) )
				{
					SoundManager.PlayNetworkedAtPos( "GrillHit", worldPos, Random.Range( 0.9f, 1.1f ) );
					return AddGrillDamage( dmgAmt, data, cellPos, worldPos, attackType );
				}
			}
		}

		//fixme
//		if ( Layer.LayerType == LayerType.Objects )
//		{
//			if ( metaTileMap.HasTile( cellPos, LayerType.Objects, true ) )
//			{
////				SoundManager.PlayNetworkedAtPos( "TableHit", worldPos, Random.Range( 0.9f, 1.1f ) );
//				return AddTableDamage( dmgAmt, data, cellPos, worldPos, attackType );
//			}
//		}

		if ( Layer.LayerType == LayerType.Floors )
		{
			if ( metaTileMap.HasTile( cellPos, LayerType.Floors, true ) )
			{
//				SoundManager.PlayNetworkedAtPos( "FloorHit", worldPos, Random.Range( 0.9f, 1.1f ) );
				return AddFloorDamage( dmgAmt, data, cellPos, worldPos, attackType );
			}
		}

		if ( Layer.LayerType == LayerType.Base )
		{
			if ( metaTileMap.HasTile( cellPos, LayerType.Base, true ) )
			{
//				SoundManager.PlayNetworkedAtPos( "FloorHit", worldPos, Random.Range( 0.9f, 1.1f ) );
				return AddPlatingDamage( dmgAmt, data, cellPos, worldPos, attackType );
			}
		}

		return dmgAmt;
	}

	private float AddTableDamage( int dmgAmt, MetaDataNode data, Vector3Int cellPos, Vector2 worldPos, AttackType attackType )
	{
		data.Damage += TABLE_ARMOR.GetDamage(dmgAmt, attackType);

		if (data.Damage >= MAX_TABLE_DAMAGE)
		{
			tileChangeManager.UpdateTile(cellPos, TileType.Object, null); //fixme: watch out! must not destroy other objects like player!

//			SoundManager.PlayNetworkedAtPos("TableHit", worldPos, 1f);

			//todo: Spawn wood or metal

			return data.ResetDamage() - MAX_TABLE_DAMAGE;
		}

		return 0;
	}

	private float AddWallDamage( float dmgAmt, MetaDataNode data, Vector3Int cellPos, Vector2 worldPos, AttackType attackType )
	{
		data.Damage += WALL_ARMOR.GetDamage(dmgAmt, attackType);

		if (data.Damage >= MAX_WALL_DAMAGE)
		{
			tileChangeManager.RemoveTile(cellPos, LayerType.Walls);

//			SoundManager.PlayNetworkedAtPos("WallHit", worldPos, 1f);

			//Spawn remains:
			if ( Random.value < 0.05f )
			{
				SpawnRods(worldPos);
			}
			else if ( Random.value > 0.95f )
			{
				SpawnMetal(worldPos);
			}

			return data.ResetDamage() - MAX_WALL_DAMAGE;
		}

		return 0;
	}

	private float AddFloorDamage( float dmgAmt, MetaDataNode data, Vector3Int cellPos, Vector2 worldPos, AttackType attackType )
	{
		data.Damage += FLOOR_ARMOR.GetDamage(dmgAmt, attackType);

		if ( data.Damage >= 30 && data.Damage < 70 )
		{
			TryScorch( cellPos );
		}
		else if (data.Damage >= MAX_FLOOR_DAMAGE)
		{
			tileChangeManager.RemoveTile(cellPos, LayerType.Floors);
			if ( Random.value < 0.05f )
			{
				CraftingManager.Construction.SpawnFloorTile(Vector3Int.RoundToInt(worldPos), null); // TODO parent ?
			}

//			SoundManager.PlayNetworkedAtPos("FloorHit", worldPos, 1f);

			return data.ResetDamage() - MAX_FLOOR_DAMAGE;
		}

		return 0;
	}

	/// <summary>
	/// Damage Plating/Catwalk/Lattice
	/// </summary>
	/// <param name="dmgAmt"></param>
	/// <param name="data"></param>
	/// <param name="cellPos"></param>
	/// <param name="worldPos"></param>
	/// <param name="attackType"></param>
	private float AddPlatingDamage( float dmgAmt, MetaDataNode data, Vector3Int cellPos, Vector2 worldPos, AttackType attackType )
	{
		data.Damage += BASE_ARMOR.GetDamage(dmgAmt, attackType);

		if ( data.Damage >= 30 && data.Damage < MAX_PLATING_DAMAGE )
		{
			TryScorch( cellPos );
		}
		else if (data.Damage >= MAX_PLATING_DAMAGE)
		{
			tileChangeManager.RemoveTile(cellPos, LayerType.Base);
			//Spawn remains:
			if ( Random.value < 0.05f )
			{
				SpawnRods(worldPos);
			}
			else if ( Random.value > 0.95f )
			{
				SpawnMetal(worldPos);
			}

//			SoundManager.PlayNetworkedAtPos("PlatingHit", worldPos, 1f);

			return data.ResetDamage() - MAX_PLATING_DAMAGE;
		}

		return 0;
	}

	private float AddWindowDamage(float damage, MetaDataNode data, Vector3Int cellPos, Vector3 bulletHitTarget, AttackType attackType)
	{
		data.Damage += REINFORCED_WINDOW_ARMOR.GetDamage(damage, attackType);
		if (data.Damage >= 20 && data.Damage < 50 && data.WindowDmgType != "crack01")
		{
			tileChangeManager.UpdateTile(cellPos, TileType.WindowDamaged, "crack01");
			data.WindowDmgType = "crack01";
		}

		if (data.Damage >= 50 && data.Damage < 75 && data.WindowDmgType != "crack02")
		{
			tileChangeManager.UpdateTile(cellPos, TileType.WindowDamaged, "crack02");
			data.WindowDmgType = "crack02";
		}

		if (data.Damage >= 75 && data.Damage < MAX_WINDOW_DAMAGE && data.WindowDmgType != "crack03")
		{
			tileChangeManager.UpdateTile(cellPos, TileType.WindowDamaged, "crack03");
			data.WindowDmgType = "crack03";
		}

		if (data.Damage >= MAX_WINDOW_DAMAGE && data.WindowDmgType != "broken")
		{
			tileChangeManager.UpdateTile(cellPos, TileType.WindowDamaged, "none");
			tileChangeManager.RemoveTile(cellPos, LayerType.Windows);

			//Spawn 3 glass shards with different sprites:
			SpawnGlassShards(bulletHitTarget);

			//Play the breaking window sfx:
			SoundManager.PlayNetworkedAtPos("GlassBreak0" + Random.Range(1, 4).ToString(), bulletHitTarget, 1f);

			data.WindowDmgType = "broken";
			return data.ResetDamage() - MAX_WINDOW_DAMAGE;
		}

		return 0;
	}

	private float AddGrillDamage(float damage, MetaDataNode data, Vector3Int cellPos, Vector3 bulletHitTarget, AttackType attackType)
	{
		data.Damage += GRILL_ARMOR.GetDamage(damage, attackType);

		//Make grills a little bit weaker (set to 60 hp):
		if (data.Damage >= MAX_GRILL_DAMAGE)
		{
			tileChangeManager.RemoveTile(cellPos, LayerType.Grills);
			tileChangeManager.UpdateTile(cellPos, TileType.WindowDamaged, "GrillDestroyed");

			SoundManager.PlayNetworkedAtPos("GrillHit", bulletHitTarget, 1f);

			//Spawn rods:
			SpawnRods(bulletHitTarget);

			return data.ResetDamage() - MAX_GRILL_DAMAGE;
		}

		return 0;
	}

	//Only works server side:
	public void WireCutGrill(Vector3 snipPosition)
	{
		Vector3Int cellPos = metaTileMap.WorldToCell(snipPosition);
		MetaDataNode data = metaDataLayer.Get(cellPos);

		if (Layer.LayerType == LayerType.Grills)
		{
			//Make sure a window is not protecting it first:
			if (!metaTileMap.HasTile(cellPos, LayerType.Windows, true))
			{
				if (metaTileMap.HasTile(cellPos, LayerType.Grills, true))
				{
					tileChangeManager.RemoveTile(cellPos, LayerType.Grills);

					SoundManager.PlayNetworkedAtPos("WireCutter", snipPosition, 1f);
					SpawnRods(snipPosition);
				}
			}
		}

		data.ResetDamage();
	}

	private void SpawnMetal(Vector3 pos)
	{
		ObjectFactory.SpawnMetal(1, pos.RoundToInt().To2Int());
	}
	private void SpawnRods(Vector3 pos)
	{
		ObjectFactory.SpawnRods(1, pos.RoundToInt().To2Int());
	}

	private void SpawnGlassShards(Vector3 pos)
	{
		//Spawn 3 glass shards with different sprites:
		ObjectFactory.SpawnGlassShard(3, pos.To2Int());

		//Play the breaking window sfx:
		SoundManager.PlayNetworkedAtPos("GlassBreak0" + Random.Range(1, 4), pos, 1f);
	}

	public void OnExposed(FireExposure exposure)
	{
		var cellPos = exposure.ExposedLocalPosition.To3Int();
		if (Layer.LayerType == LayerType.Floors)
		{
			//floor scorching
			if (exposure.IsSideExposure) return;
			if (!(exposure.Temperature > TILE_MIN_SCORCH_TEMPERATURE)) return;

			if (!metaTileMap.HasTile(cellPos, true)) return;
			TryScorch( cellPos );
		}
		else if (Layer.LayerType == LayerType.Windows)
		{
			if (metaTileMap.HasTile(cellPos, LayerType.Windows, true))
			{
				//window damage
				SoundManager.PlayNetworkedAtPos("GlassHit", exposure.ExposedWorldPosition.To3Int(), Random.Range(0.9f, 1.1f));
				AddWindowDamage(exposure.StandardDamage(), metaDataLayer.Get(cellPos), cellPos, exposure.ExposedWorldPosition.To3Int(), AttackType.Melee);
				return;
			}

		}
		else if (Layer.LayerType == LayerType.Grills)
		{
			//grill damage
			//Make sure a window is not protecting it first:
			if (!metaTileMap.HasTile(cellPos, LayerType.Windows, true))
			{
				if (metaTileMap.HasTile(cellPos, LayerType.Grills, true))
				{
					SoundManager.PlayNetworkedAtPos("GrillHit", exposure.ExposedWorldPosition.To3Int(), Random.Range(0.9f, 1.1f));
					AddGrillDamage(exposure.StandardDamage(), metaDataLayer.Get(cellPos), cellPos, exposure.ExposedWorldPosition.To3Int(), AttackType.Melee);
				}
			}
		}
	}

	//TODO: make use of BasicTile fields: float MaxHealth; TileState[] HealthStates; LayerTile DestroyedTile
	public void TryScorch( Vector3Int cellPos )
	{
		//is it already scorched
		var metaData = metaDataLayer.Get( cellPos );
		if ( metaData.IsScorched )
			return;

		//TODO: This should be done using an overlay system which hasn't been implemented yet, this replaces the tile's original appearance
		if ( metaTileMap.HasTile( cellPos, LayerType.Floors, true ) )
		{ //Scorch floors
			tileChangeManager.UpdateTile( cellPos, TileType.Floor, "floorscorched"+Random.Range( 1,3 ) );
		}
		else
		{ //Scorch base
			tileChangeManager.UpdateTile( cellPos, TileType.Base, "platingdmg"+Random.Range( 1,4 ) );
		}

		metaData.IsScorched = true;
	}
}