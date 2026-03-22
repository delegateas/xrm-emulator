# XML Format Reference

## SavedQuery (View) — `{guid}.xml`

```xml
<savedqueries>
  <savedquery>
    <savedqueryid>{guid}</savedqueryid>
    <querytype>0</querytype>           <!-- 0=public, 64=lookup, 4096=quickfind -->
    <isdefault>0</isdefault>
    <layoutxml>
      <grid name="resultset" object="OBJECTTYPECODE" jump="primaryfield" select="1" icon="1" preview="1">
        <row name="result" id="primaryidfield">
          <cell name="columnlogicalname" width="300" />
          <!-- more cells = more columns in the view -->
        </row>
      </grid>
    </layoutxml>
    <fetchxml>
      <fetch version="1.0" mapping="logical">
        <entity name="logicalname">
          <attribute name="..." />
          <order attribute="..." descending="false" />
          <filter type="and">
            <condition attribute="..." operator="eq" value="..." />
          </filter>
        </entity>
      </fetch>
    </fetchxml>
    <LocalizedNames>
      <LocalizedName description="View Name" languagecode="1030" />
    </LocalizedNames>
  </savedquery>
</savedqueries>
```

**Query types:** 0 = Public View, 1 = Saved View, 2 = Associated, 64 = Lookup, 4096 = Quick Find

**IMPORTANT — `object` attribute on `<grid>`:** The `object` attribute is the entity's integer ObjectTypeCode and is **required** by Dataverse. Look up the ObjectTypeCode from `Model/entities.md`. The `views new` scaffold includes it automatically. Common codes: Account=1, Contact=2. For custom entities, always check `Model/entities.md`.

## AppModuleSiteMap

```xml
<AppModuleSiteMap>
  <SiteMap>
    <Area Id="area_id">
      <Title LCID="1030" Title="Area Name" />
      <Group Id="group_id">
        <Title LCID="1030" Title="Group Name" />
        <SubArea Id="subarea_id" Entity="logicalname" />
        <!-- More SubAreas for more entities in the group -->
      </Group>
    </Area>
  </SiteMap>
</AppModuleSiteMap>
```
